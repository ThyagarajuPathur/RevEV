# RPM Smoothing for Engine Audio Simulation

## The Problem

The ELM327 OBD adapter polls RPM from the car's BMS ECU (7E4) at ~14 Hz (one reading every ~70ms). Each reading is a discrete integer that can jump 100-400 RPM between consecutive samples:

```
Time (ms):    0      70     140    210    280    350
OBD RPM:    1500   1700   2000   2200   2500   2800
```

The audio engine needs smooth, continuous RPM values at 60 fps (every ~17ms) to control playback rate and gain crossfades. If we feed the raw OBD values directly, the audio "steps" audibly every 70ms.

---

## Current Implementation (Velocity Extrapolation + 3-Layer Smoothing)

**File:** `EVEngineSound/Services/Audio/EngineAudioEngine.swift`

### Data Flow

```
OBD (14 Hz)                     Display Link (60 fps)                Audio Mapper
    |                                   |                                 |
    |-- update(rpm:) ----+              |                                 |
    |   sets targetRPM   |              |                                 |
    |   sets targetRate   |              |                                 |
    |                    |              |                                 |
    |                    +-- rpmRate smooths toward targetRate (6%/frame)  |
    |                    |   renderedRPM += rpmRate * dt                  |
    |                    |   renderedRPM drifts toward targetRPM (2%/frame)|
    |                    |   outputRPM = EMA(renderedRPM, 12%/frame)      |
    |                    |              |                                 |
    |                    |              +--- outputRPM --> mapper -------->|
```

### Layer 1: Velocity Estimation (in `update()`, called at ~14Hz)

When a new OBD reading arrives, we compute how fast RPM is changing:

```swift
let instantRate = (newRPM - prevOBDRpm) / timeDelta  // RPM per second
targetRate = targetRate * 0.5 + instantRate * 0.5    // smooth the target
```

**Purpose:** Gives us a direction and speed to extrapolate between readings.

**Example:** OBD reads 2000, then 2200 after 70ms:
- `instantRate = (2200 - 2000) / 0.07 = 2857 RPM/sec`
- This becomes the velocity we use between readings.

### Layer 2: Velocity Smoothing (in `displayLinkFired()`, every frame at 60fps)

The actual `rpmRate` used for extrapolation doesn't jump to `targetRate` instantly. It glides toward it:

```swift
rpmRate += (targetRate - rpmRate) * 0.06  // 6% per frame
```

**Purpose:** Prevents sudden velocity changes when a new OBD reading arrives. Without this, the RPM curve would have visible "kinks" at each OBD update.

**Effect:** Velocity changes spread over ~15 frames (~250ms) instead of happening in one frame.

### Layer 3: Position Extrapolation + Drift Correction (every frame)

```swift
// Move rendered RPM at the current smooth velocity
renderedRPM += rpmRate * dt

// Gently steer toward the last known actual OBD value
renderedRPM += (targetRPM - renderedRPM) * 0.02  // 2% per frame
```

**Purpose:**
- Extrapolation fills the ~70ms gap between OBD readings with smooth continuous motion.
- Correction prevents drift — if the velocity estimate is slightly wrong, the rendered value doesn't wander away from reality.

**Example at 60fps between two OBD readings:**
```
Frame 1:  renderedRPM = 2000 + (2857 * 0.017) = 2049
Frame 2:  renderedRPM = 2049 + (2857 * 0.017) = 2097
Frame 3:  renderedRPM = 2097 + (2857 * 0.017) = 2146
Frame 4:  renderedRPM = 2146 + (2857 * 0.017) = 2194
         (next OBD reading arrives: 2200 — very close!)
```

### Layer 4: Final Output EMA (every frame)

```swift
outputRPM += (renderedRPM - outputRPM) * 0.12  // 12% per frame
```

**Purpose:** Last-mile smoothing. Catches any micro-kinks from velocity changes or correction adjustments. This is what the audio mapper actually reads.

---

## Tunable Parameters

| Parameter | Current | Effect of lowering | Effect of raising |
|-----------|---------|-------------------|-------------------|
| `rateSmoothingPerFrame` | 0.06 | Smoother velocity transitions, more lag | Faster velocity response, more kinks |
| `correctionFactor` | 0.02 | Less snapping to OBD, more drift | Tighter tracking, more jumps |
| `outputSmoothing` | 0.12 | Smoother final output, more lag | Less lag, micro-kinks audible |
| `targetRate` blend (0.5) | 0.5 | Slower velocity adaptation | Faster but jumpier velocity |

---

## Known Limitation: Overshoot on Direction Change

The biggest remaining issue occurs when the driver suddenly changes intent:

```
Time:       t0    t1    t2    t3    t4    t5    t6
OBD RPM:   1500  2000  2500  [USER BRAKES]  2000  1500
                                  |
                        Code still extrapolating UP
                        because velocity = +7000 RPM/sec
                                  |
                        renderedRPM reaches ~2800
                        before OBD reading at t4 (2000)
                        corrects it back down
                                  |
                        Audible "hump" / overshoot
```

**Why this happens:** The code only knows past velocity. It has no way to detect that the driver's intent changed (gas released, brake pressed) until the next OBD RPM reading arrives 70ms later. By then, the rendered value has already overshot.

**How severe:** At +7000 RPM/sec extrapolation, 70ms of overshoot = ~490 RPM past the peak before correction kicks in.

---

## Proposed Fix: Accelerator & Brake Pedal as Leading Indicators

### Why Pedal Data Helps

The accelerator and brake pedals are **leading indicators** — they change BEFORE RPM does:

```
Time:       t0    t1    t2    t3    t4    t5    t6
Accel:      80%   90%   95%   [RELEASE] 0%   0%    0%
Brake:       0%    0%    0%   [PRESS]  30%  60%   80%
RPM:       1500  2000  2500   2500    2300  2000  1500
                                |
                    Pedal drops to 0% IMMEDIATELY
                    (in the same OBD reading as last high RPM)
                    Code detects intent change, stops extrapolating up
```

The pedal changes in the **same OBD cycle** as the intent change, while RPM lags behind by 1-2 cycles due to engine/motor inertia.

### Proposed Implementation

#### 1. Data Acquisition

Need two additional values from OBD, polled alongside RPM:
- **Accelerator pedal position** (0-100%): How much gas the driver is requesting.
- **Brake pedal position or switch** (0-100% or on/off): Whether the driver is braking.

These may come from:
- Same `220101` BMS response at different byte offsets (if available)
- Different ECU/DID (e.g., VMCU at 7E2 with DID 2101 or 220101)
- Standard OBD-II PID `0149` (accelerator pedal position D)

#### 2. Intent Detection Logic

Classify the driver's intent from pedal state:

```swift
enum DriverIntent {
    case accelerating   // accel pedal > 10%
    case coasting       // accel pedal < 10%, brake off
    case braking        // brake pedal active
}

func detectIntent(accelPedal: Double, brakePedal: Double) -> DriverIntent {
    if brakePedal > 0.05 { return .braking }
    if accelPedal > 0.10 { return .accelerating }
    return .coasting
}
```

#### 3. Intent-Aware Velocity Management

When `update(rpm:accelPedal:brakePedal:)` is called:

```swift
let newIntent = detectIntent(accelPedal: accelPedal, brakePedal: brakePedal)

if newIntent != previousIntent {
    // Intent changed! Immediately adjust velocity behavior.
    switch newIntent {
    case .braking:
        // Kill positive velocity, set a negative bias
        targetRate = min(targetRate, 0)
        // Increase correction factor temporarily for faster convergence
        temporaryCorrectionBoost = true

    case .coasting:
        // RPM will naturally decrease; dampen velocity toward zero
        targetRate *= 0.3

    case .accelerating:
        // Allow normal velocity extrapolation
        break
    }
    previousIntent = newIntent
}
```

#### 4. Pedal-Modulated Extrapolation Confidence

Use pedal magnitude to scale how aggressively we extrapolate:

```swift
// Higher pedal = more confident extrapolation
let confidence = accelPedal  // 0.0 to 1.0

// Scale extrapolation by confidence
renderedRPM += rpmRate * dt * confidence

// When pedal is 0, no extrapolation — just drift toward actual
// When pedal is 100%, full extrapolation
```

This naturally handles:
- **Full throttle:** Confident extrapolation, smooth acceleration
- **Partial throttle:** Moderate extrapolation
- **Off throttle:** No extrapolation, just track actual RPM
- **Braking:** Reverse extrapolation

#### 5. Expected Improvement

| Scenario | Without pedal | With pedal |
|----------|--------------|------------|
| Steady acceleration | Smooth (works well) | Smooth (same) |
| Gas → brake | Overshoots ~500 RPM, audible hump | Immediately stops upward motion, smooth transition down |
| Brake → gas | Undershoots ~300 RPM | Immediately allows upward extrapolation |
| Quick blip (gas-brake-gas) | Double overshoot | Clean tracking, minimal overshoot |
| Coasting | Slight drift | Tracks closely, no extrapolation |

#### 6. Required PID Information

To implement this, we need from the Hyundai/Kia PID database:

| Data | Format example | Needed info |
|------|---------------|-------------|
| Accelerator pedal % | `XXX_Accel_Pedal,Pedal Depth,0x220101,(byte)/2,0,100,%,7E2` | ECU, DID, byte offset, formula |
| Brake pedal % or switch | `XXX_Brake,Brake Active,0x220101,bit(byte,N),0,1,bool,7E4` | ECU, DID, byte offset, formula |

These are typically found in:
- Hyundai/Kia Torque Pro PID CSV files
- OpenVehicles / EVNotify PID databases
- SoulEV / Ioniq / EV6 community OBD docs

---

## Alternative Approaches Considered

### 1. Lower EMA (Simple Low-Pass Filter)
```swift
smoothedRPM = smoothedRPM * 0.95 + rawRPM * 0.05
```
**Rejected:** Adds significant lag (time constant ~1.3 seconds). RPM response feels sluggish and disconnected from actual driving.

### 2. Lerp Per Frame
```swift
currentRPM += (targetRPM - currentRPM) * 0.15
```
**Rejected:** Still feels like discrete steps. The lerp closes the gap exponentially, so it moves fast at first (audible snap) then slows down.

### 3. Rate Limiter (Max RPM/sec)
```swift
let maxStep = 3000.0 * dt  // 3000 RPM/sec cap
currentRPM += clamp(diff, -maxStep, maxStep)
```
**Rejected:** Creates linear ramps between OBD readings. Abrupt slope changes when new reading arrives. Better than raw steps, but still audible.

### 4. Critically Damped Spring
```swift
let accel = omega^2 * (target - current) - 2 * omega * velocity
velocity += accel * dt
current += velocity * dt
```
**Rejected:** Spring settles too fast at useful frequencies (4Hz = 120ms settle). By the time the next OBD reading arrives, the spring has already settled, so it still feels like steps.

### 5. Velocity Extrapolation (Current Approach)
Extrapolate using estimated RPM velocity between OBD readings, with drift correction and output smoothing.
**Selected:** Best balance of smoothness and responsiveness. Main weakness is overshoot on direction changes, which pedal data would fix.

---

## Summary

The current 3-layer smoothing system (velocity smoothing + extrapolation + output EMA) provides good continuous audio between OBD readings but overshoots when the driver changes between accelerating and braking. Adding accelerator and brake pedal data as leading indicators would eliminate this overshoot by detecting intent changes immediately, before they show up in RPM.
