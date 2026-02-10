# F-023: Real Pedal Data + Decoupled Audio Engine

## Problem

Two related issues:

1. **Hardcoded throttle**: `EngineAudioEngine.update()` set `renderedPedal = 1.0` (always 100%), making the throttle-axis crossfade in `RPMAudioMapper` always behave as full-throttle. Off-throttle engine sound layers (`offLow`, `offHigh`) never played.

2. **RPM overshoot on direction change**: When the driver lifts off the accelerator, the RPM extrapolation system continues projecting upward for ~70ms until the next OBD reading arrives. At +7000 RPM/sec extrapolation, this causes ~500 RPM overshoot — an audible "hump" before the correction kicks in.

## Solution

### Accelerator Pedal Data (Bytes 10-11 from 7E4 220101)

The Hyundai/Kia BMS ECU (7E4) DID 0x0101 response contains pedal position at data bytes 10-11:

```
Response: 62 01 01 [data byte 0] ... [data byte 10] [data byte 11] ...
                                       ^high byte      ^low byte
```

- **Acceleration range (12-bit)**: byte 10 goes 0→15, byte 11 goes 0→255
  - Raw = `byte10 * 256 + byte11`, range 0→4095
  - Normalized to 0.0–1.0 by dividing by 4095
- **Deceleration/regen**: byte 10 reads 255 downward (raw ≥ 4096)
  - Treated as 0% throttle for the audio engine

| Pedal press | Byte 10 | Byte 11 | Raw  | Percentage |
|-------------|---------|---------|------|------------|
| None        | 0       | 0       | 0    | 0%         |
| Slight      | 0       | 120     | 120  | ~2.9%      |
| Medium      | 8       | 0       | 2048 | ~50%       |
| Full        | 15      | 255     | 4095 | 100%       |
| Decel/regen | 255     | 200     | 65480| 0% (regen) |

### Unified OBD Polling

`OBDService` parses **both RPM and pedal from a single `220101` response** via `pollBMS()`, doubling the effective update rate from ~7Hz per value to ~14Hz for both.

### Decoupled Audio Architecture (First-Principles Rewrite)

After multiple iterations of intent-detection approaches (all created audio artifacts at transition points), the engine was rewritten from first principles:

**Two independent systems, updated at ~60 Hz by CADisplayLink:**

1. **RPM → Pitch**: Velocity extrapolation between OBD readings fills 70ms gaps with smooth, continuous RPM. Drift correction and output EMA prevent divergence.

2. **Pedal → Layer Balance**: Smoothed pedal position drives the throttle-axis crossfade (on-layers vs off-layers). Equal-power cosine crossfade keeps perceived volume constant.

**Single coupling point**: pedal position scales extrapolation confidence. When pedal is released, confidence drops naturally, limiting RPM overshoot — no special-case logic or intent detection needed.

Key design decisions:
- **No intent detection** — removed `DriverIntent` enum, `coastingThreshold`, `targetRate` capping, `rpmRate` manipulation. All these created artifacts at transition boundaries.
- **Single symmetric pedal EMA** (0.15/frame ≈ 100ms) — no asymmetric rates needed because equal-power cosine crossfade maintains constant perceived volume.
- **`update()` just stores targets** — all smoothing happens in the display link at 60fps. No special-case logic in the OBD callback.

### Equal-Power Cosine Crossfade

Both RPM axis and throttle axis use `sin²+cos²=1` crossfade:
```
onGain  = sin(pedal * π/2)
offGain = cos(pedal * π/2)
```
This maintains constant perceived volume through the entire on→off transition, eliminating the -3dB energy dip that linear crossfade causes.

## Files Changed

| File | Change |
|------|--------|
| `EVEngineSound/Services/OBD/PIDParser.swift` | Bytes 10-11 pedal parsing: 12-bit accel range (0-4095), regen detection |
| `EVEngineSound/Services/OBD/OBDService.swift` | Unified polling via `pollBMS()` for both RPM+pedal |
| `EVEngineSound/Services/Audio/EngineAudioEngine.swift` | First-principles rewrite: decoupled RPM/pedal systems, no intent detection |
| `EVEngineSound/Services/Audio/RPMAudioMapper.swift` | Equal-power cosine crossfade on throttle axis |
| `EVEngineSoundTests/PIDParserTests.swift` | Updated tests for 12-bit range + deceleration case |

## Tuning Notes

- **`pedalRawMax` (4095)**: 12-bit based on byte 10 reaching 15 during full acceleration
- **`regenThreshold` (4096)**: Raw values at or above this are deceleration/regen → 0% throttle
- **`pedalSmoothing` (0.15)**: EMA alpha per frame. Higher = faster response, lower = smoother transition
- **`correctionFactor` (0.02)**: How aggressively rendered RPM drifts toward actual OBD value per frame
- **`outputSmoothing` (0.12)**: Final EMA on output RPM before feeding to mapper
