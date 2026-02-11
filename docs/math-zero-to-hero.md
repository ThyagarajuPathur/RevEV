# Zero to Hero: The Math Behind RevEV

A ground-up guide to every math concept used in the RevEV engine sound
simulator — from basic arithmetic to equal-power crossfades. No prior math
knowledge assumed.

---

## Table of Contents

1. [Level 0 — Building Blocks](#level-0--building-blocks)
2. [Level 1 — Ratios and Normalization](#level-1--ratios-and-normalization)
3. [Level 2 — Angles, Circles, and Radians](#level-2--angles-circles-and-radians)
4. [Level 3 — Trigonometry (sin, cos)](#level-3--trigonometry-sin-cos)
5. [Level 4 — The Pythagorean Identity](#level-4--the-pythagorean-identity)
6. [Level 5 — Interpolation](#level-5--interpolation)
7. [Level 6 — Equal-Power Crossfade](#level-6--equal-power-crossfade)
8. [Level 7 — Exponential Moving Average (EMA)](#level-7--exponential-moving-average-ema)
9. [Level 8 — Velocity and Extrapolation](#level-8--velocity-and-extrapolation)
10. [Level 9 — Putting It All Together](#level-9--putting-it-all-together)
11. [Appendix A — Quick Reference Card](#appendix-a--quick-reference-card)
12. [Appendix B — Code ↔ Math Map](#appendix-b--code--math-map)

---

## Level 0 — Building Blocks

### Multiplication as scaling

Multiplying by a number between 0 and 1 **shrinks** a value. Multiplying by
a number greater than 1 **stretches** it.

```
100 × 0.5  = 50     ← half
100 × 1.0  = 100    ← unchanged
100 × 2.0  = 200    ← doubled
```

**In RevEV:** Every audio layer has a "gain" (volume) between 0.0 and 1.0.
The audio hardware takes the raw sample and multiplies each value by the gain.
Gain 0.0 = silence. Gain 1.0 = full volume.

### Division as "how many times does it fit?"

```
6 / 3 = 2       ← "3 fits into 6 two times"
4500 / 9000 = 0.5   ← "4500 is half of 9000"
```

**In RevEV:** `rpm / maxRPM` answers "what fraction of the maximum are we at?"

### Clamping — keeping a value in bounds

Sometimes a calculation can produce a number outside a useful range. Clamping
forces it back inside.

```
clamp(value, min, max):
    if value < min → return min
    if value > max → return max
    otherwise      → return value

clamp(-5, 0, 1)   = 0      ← below floor, snapped up
clamp(0.7, 0, 1)  = 0.7    ← already in range, untouched
clamp(3.2, 0, 1)  = 1      ← above ceiling, snapped down
```

**In RevEV:** Used everywhere — playback rate clamped to [0.1, 4.0],
crossfade `t` clamped to [0, 1], RPM clamped to [0, maxRPM].

**Code reference:** `RPMAudioMapper.swift:122-124`

---

## Level 1 — Ratios and Normalization

### What is a ratio?

A ratio compares two quantities. "RPM is 4500 out of 9000" is a ratio.
Written as a fraction: `4500 / 9000 = 0.5`.

### Normalization — mapping any range to 0–1

Normalization answers: **"Where does a value sit between a minimum and a
maximum, on a 0-to-1 scale?"**

```
normalized = (value - min) / (max - min)
```

**Example:** Temperature sensor reads 25°C. Range is 10°C to 50°C.

```
normalized = (25 - 10) / (50 - 10) = 15 / 40 = 0.375
```

So 25°C is 37.5% of the way from min to max.

**Example with RPM:** Low anchor = 5300, high anchor = 7900, current = 6600.

```
t = (6600 - 5300) / (7900 - 5300) = 1300 / 2600 = 0.5
```

The engine is exactly halfway between the two anchor RPMs.

**Why this matters:** Once you have a 0-to-1 value, you can plug it into
any formula (crossfade, color gradient, gauge position) without worrying
about the original units. This is the single most important concept in
the entire project.

**Code reference:** `RPMAudioMapper.swift:28`

### Proportional zones (the gauge segments)

The RPM gauge divides its colored arc into zones defined as fractions of
`maxRPM`:

```
Green:  0%  to 40%   (0.00 to 0.40)
Yellow: 40% to 70%   (0.40 to 0.70)
Orange: 70% to 88%   (0.70 to 0.88)
Red:    88% to 100%  (0.88 to 1.00)
```

For a 9000 RPM profile: green ends at `9000 × 0.4 = 3600 RPM`.
For an 8500 RPM profile: green ends at `8500 × 0.4 = 3400 RPM`.

Same fractions, different profiles, always proportionally correct.

**Code reference:** `RPMGaugeView.swift:32-46`

---

## Level 2 — Angles, Circles, and Radians

### Degrees — the familiar way

A full circle = 360°. A right angle = 90°. Half circle = 180°.

The RPM gauge uses a **270° arc** (three-quarters of a circle), starting at
135° and ending at 405° (which is 135° + 270°). The missing 90° is the gap
at the bottom where the RPM readout sits.

```
        0°
        |
  270°--+--90°     ← standard compass
        |
       180°

Gauge arc: 135° → 405° (= 45° past full circle)
That's the left-bottom around the top to the right-bottom.
```

### Radians — the math way

Radians measure angles using the **radius** of the circle. If you wrap the
radius along the circle's edge, the angle you span is **1 radian**.

```
Full circle = 2π radians  ≈ 6.283 radians
Half circle = π radians   ≈ 3.14159
Quarter     = π/2 radians ≈ 1.5708
```

**Conversion:**

```
radians = degrees × (π / 180)
degrees = radians × (180 / π)
```

**Example:** 90° → `90 × π/180 = π/2 ≈ 1.5708` radians.

**Why radians?** Every math library (Swift's `sin()`, `cos()`) expects
radians. The code converts like this:

```swift
let rad = (angle - 90) * .pi / 180
```

(The `- 90` rotates the coordinate system so 0° points up instead of right.)

**Code reference:** `RPMGaugeView.swift:130`, `RPMAudioMapper.swift:29`

---

## Level 3 — Trigonometry (sin, cos)

### The unit circle — one picture that explains everything

Draw a circle with radius 1 (the "unit circle"). Pick a point on it by
choosing an angle θ from the rightward horizontal. That point's coordinates
are:

```
x = cos(θ)
y = sin(θ)
```

```
              (0, 1)          ← cos(90°)=0, sin(90°)=1
                |
                |    • ← point at angle θ
                |   /
                |  / radius = 1
                | / θ
 (-1,0) -------+-------(1, 0)   ← cos(0°)=1, sin(0°)=0
                |
                |
              (0, -1)         ← cos(270°)=0, sin(270°)=-1
```

### Key values to memorize

| Angle   | cos    | sin    |
|---------|--------|--------|
| 0°      | 1.000  | 0.000  |
| 30°     | 0.866  | 0.500  |
| 45°     | 0.707  | 0.707  |
| 60°     | 0.500  | 0.866  |
| 90°     | 0.000  | 1.000  |

Notice the **symmetry**: cos at some angle = sin at (90° - that angle).
At 45°, they're equal: both 0.707.

### What sin and cos "do" intuitively

- **cos(θ)** starts at 1 and smoothly decreases to 0 as θ goes from 0° to 90°.
- **sin(θ)** starts at 0 and smoothly increases to 1 as θ goes from 0° to 90°.

They're like a **perfectly smooth seesaw**: as one goes up, the other comes
down, following a curve (not a straight line).

### How RevEV uses sin/cos for the gauge needle

To place a tick mark or label at a specific angle on the arc, the code needs
an (x, y) pixel position:

```swift
x = cos(rad) * radius
y = sin(rad) * radius
```

This converts polar coordinates (angle + distance) to cartesian coordinates
(x, y) — the same unit circle idea, just scaled by the radius.

**Code reference:** `RPMGaugeView.swift:131-142`

---

## Level 4 — The Pythagorean Identity

### The foundation: a² + b² = c²

In a right triangle, the square of the hypotenuse equals the sum of the
squares of the other two sides.

```
        /|
    c  / |  b
      /  |
     /θ__|
       a
```

```
a² + b² = c²
```

### Applied to the unit circle: sin² + cos² = 1

Since the unit circle has radius 1 (the hypotenuse), and `x = cos(θ)`,
`y = sin(θ)`:

```
cos²(θ) + sin²(θ) = 1²
cos²(θ) + sin²(θ) = 1       ← always, for any angle θ
```

Let's verify with θ = 45°:

```
cos(45°) = 0.707,  sin(45°) = 0.707
0.707² + 0.707² = 0.500 + 0.500 = 1.000  ✓
```

And θ = 30°:

```
cos(30°) = 0.866,  sin(30°) = 0.500
0.866² + 0.500² = 0.750 + 0.250 = 1.000  ✓
```

**This identity is the entire reason the crossfade works.** Read on.

---

## Level 5 — Interpolation

### Linear interpolation (LERP)

Given a 0-to-1 value `t`, blend between two values A and B:

```
result = A × (1 - t) + B × t
```

When `t = 0` → result = A (100% A, 0% B)
When `t = 0.5` → result = 0.5A + 0.5B (equal mix)
When `t = 1` → result = B (0% A, 100% B)

**Everyday example:** Mixing paint. `t = 0.3` means 70% blue + 30% red.

### The problem with linear interpolation for audio

If you blend two audio signals linearly at the midpoint:

```
gainA = 0.5,  gainB = 0.5
```

Perceived loudness is proportional to **power**, and power is proportional
to amplitude **squared**:

```
total power = 0.5² + 0.5² = 0.25 + 0.25 = 0.50
```

You lost **half the energy**. The listener hears a dip in volume right in the
middle of the crossfade. This is called the **-3dB dip** and it sounds like
the engine briefly "swallows" its own sound.

```
Volume
  1.0 ┤ *                           *
      │   *                       *
  0.5 ┤      *       ↓ DIP     *       ← linear blend
      │         *    here    *
  0.0 ┤            *       *
      └─────────────────────────────
      0.0                         1.0
                crossfade t
```

---

## Level 6 — Equal-Power Crossfade

### The solution: use cos and sin instead of (1-t) and t

```
gainA = cos(t × π/2)
gainB = sin(t × π/2)
```

As `t` sweeps from 0 to 1, the input to cos/sin sweeps from 0 to π/2 (0° to
90°). This maps perfectly onto the first quadrant of the unit circle.

### Why it works — energy conservation

Total power at any point:

```
gainA² + gainB² = cos²(t × π/2) + sin²(t × π/2) = 1
```

That's the Pythagorean identity from Level 4. The energy **never dips**.

### Comparison table

| t    | Linear A | Linear B | Linear Power | Cos A | Sin B | Cos+Sin Power |
|------|----------|----------|--------------|-------|-------|---------------|
| 0.00 | 1.00     | 0.00     | 1.00         | 1.00  | 0.00  | **1.00**      |
| 0.25 | 0.75     | 0.25     | 0.63         | 0.92  | 0.38  | **1.00**      |
| 0.50 | 0.50     | 0.50     | 0.50 ← dip! | 0.71  | 0.71  | **1.00**      |
| 0.75 | 0.25     | 0.75     | 0.63         | 0.38  | 0.92  | **1.00**      |
| 1.00 | 0.00     | 1.00     | 1.00         | 0.00  | 1.00  | **1.00**      |

```
Volume (perceived power)
  1.0 ┤ ================================  ← equal-power (flat!)
      │
  0.5 ┤        *        *                 ← linear (dips)
      │
  0.0 ┤
      └──────────────────────────────
      0.0            0.5            1.0
```

### How RevEV applies this on two axes

**RPM axis** — blends between low-RPM and high-RPM samples:

```swift
// RPMAudioMapper.swift:28-30
t = clamp((rpm - lowAnchorRPM) / (highAnchorRPM - lowAnchorRPM), 0, 1)
lowGain  = cos(t × π/2)
highGain = sin(t × π/2)
```

**Pedal axis** — blends between on-throttle and off-throttle samples:

```swift
// RPMAudioMapper.swift:37-39
pedalClamped = clamp(max(pedalPosition, 0.05), 0, 1)
onGain  = sin(pedalClamped × π/2)
offGain = cos(pedalClamped × π/2)
```

(Notice `on` uses sin and `off` uses cos — when pedal is 0 (released), sin=0
so on-layers are silent, cos=1 so off-layers are full. Makes sense: no pedal
→ hear the engine-braking sound.)

### The 2D gain matrix

Since RPM and pedal are independent axes, each layer's final gain is simply
the **product** of its RPM gain and its pedal gain:

```
                   RPM axis
              lowGain    highGain
            ┌──────────┬──────────┐
  onGain    │ on × low │ on × high│   ← accelerating sounds
  (pedal    ├──────────┼──────────┤
  axis)     │off × low │off × high│   ← coasting/braking sounds
  offGain   └──────────┴──────────┘
```

```swift
// RPMAudioMapper.swift:42-45
onLow   = onGain  × lowGain
onHigh  = onGain  × highGain
offLow  = offGain × lowGain
offHigh = offGain × highGain
```

**Example:** RPM 6600 (t=0.5 on 458), pedal at 80%.

```
lowGain  = cos(0.5 × π/2) = 0.707
highGain = sin(0.5 × π/2) = 0.707
onGain   = sin(0.8 × π/2) = 0.951
offGain  = cos(0.8 × π/2) = 0.309

onLow   = 0.951 × 0.707 = 0.672   ← loudest: accelerating, mid RPM
onHigh  = 0.951 × 0.707 = 0.672   ← equally loud at the 50% RPM point
offLow  = 0.309 × 0.707 = 0.218   ← quiet: barely coasting
offHigh = 0.309 × 0.707 = 0.218
```

---

## Level 7 — Exponential Moving Average (EMA)

### The problem: raw data is jittery

The OBD adapter sends RPM readings at ~14 Hz. Each reading can jump around
due to sensor noise. If you feed raw values directly to the audio engine,
you hear crackle and stutter.

### The solution: chase the target, don't jump to it

```
smoothed = smoothed + alpha × (target - smoothed)
```

Or equivalently:

```
smoothed = smoothed × (1 - alpha) + target × alpha
```

- `alpha` = smoothing factor, between 0 and 1
- Small alpha (0.02) = slow, very smooth, sluggish
- Large alpha (0.5) = fast, less smooth, responsive

### Step-by-step example

Target = 100. Start at 0. Alpha = 0.15 (the pedal smoothing value).

```
Frame 0:  smoothed = 0
Frame 1:  smoothed = 0    + 0.15 × (100 - 0)    = 15.00
Frame 2:  smoothed = 15   + 0.15 × (100 - 15)   = 27.75
Frame 3:  smoothed = 27.75+ 0.15 × (100 - 27.75)= 38.59
Frame 4:  smoothed = 38.59+ 0.15 × (100 - 38.59)= 47.80
Frame 5:  smoothed = 47.80+ 0.15 × (100 - 47.80)= 55.63
...
Frame 10: smoothed ≈ 80.3
Frame 15: smoothed ≈ 91.3
Frame 20: smoothed ≈ 96.1
```

```
100 ┤                              ___________
    │                         ____/
    │                    ____/
    │               ____/
    │          ____/
    │     ____/                    ← smooth approach, no overshoot
    │____/
  0 ┤
    └────────────────────────────────────
    0    5    10   15   20   25   30
                   Frame
```

It approaches quickly at first (big gap = big step), then slows down as it
gets close (small gap = small step). **No overshoot, no oscillation.**

### How RevEV uses EMA at different speeds

| What                  | Alpha | Feel           | Code reference                        |
|-----------------------|-------|----------------|---------------------------------------|
| RPM rate smoothing    | 0.06  | Very smooth    | `EngineAudioEngine.swift:295`         |
| RPM drift correction  | 0.02  | Glacially slow | `EngineAudioEngine.swift:304`         |
| RPM output smoothing  | 0.12  | Moderate       | `EngineAudioEngine.swift:310`         |
| Pedal smoothing       | 0.15  | Responsive     | `EngineAudioEngine.swift:317`         |

The RPM pipeline uses **three cascaded EMAs** (rate → drift → output) to get
ultra-smooth pitch changes while still tracking the real OBD data. Think of it
like three springs in series, each absorbing a different frequency of jitter.

### Settle time intuition

An EMA with alpha `a` at 60 fps reaches ~95% of its target in roughly:

```
frames ≈ 3 / a
seconds ≈ 3 / (a × 60)
```

| Alpha | Frames to 95% | Time at 60fps |
|-------|---------------|---------------|
| 0.02  | 150           | 2.5 sec       |
| 0.06  | 50            | 0.83 sec      |
| 0.12  | 25            | 0.42 sec      |
| 0.15  | 20            | 0.33 sec      |

---

## Level 8 — Velocity and Extrapolation

### The problem: gaps between OBD readings

OBD sends data at ~14 Hz (every ~70ms). The display link runs at 60 Hz
(every ~16ms). That's roughly 4 audio frames per OBD reading. Without
filling those gaps, the audio pitch would update in visible "staircase"
steps.

```
RPM
 5000 ┤        ■                  ■
      │
 4000 ┤  ■                 ■
      │
 3000 ┤                                   ← OBD readings (14 Hz)
      └──────────────────────────────
        0ms   70ms  140ms  210ms  280ms
```

### Velocity = change / time

When two OBD readings arrive:

```
velocity = (newRPM - oldRPM) / (newTime - oldTime)
```

**Example:** RPM goes from 4000 to 4500 in 70ms (0.07 seconds):

```
velocity = (4500 - 4000) / 0.07 = 7143 RPM/sec
```

The engine is gaining ~7143 RPM per second.

**Code reference:** `EngineAudioEngine.swift:171-172`

### Extrapolation = current + velocity × time

Between OBD readings, each 60 Hz frame predicts:

```
renderedRPM += velocity × dt
```

Where `dt` ≈ 0.0167 sec (1/60).

```
RPM
 5000 ┤        ■ · · · · · · · · ■        ← dots are extrapolated
      │      · ·                 · ·
 4000 ┤  ■ · ·             ■ · ·
      │    ·                  ·
 3000 ┤                                   ← smooth 60 Hz curve
      └──────────────────────────────
        0ms   70ms  140ms  210ms  280ms
```

### Confidence scaling — the clever part

Extrapolation can overshoot if the driver suddenly lifts the pedal. RevEV
solves this without any explicit "pedal released" detection:

```swift
// EngineAudioEngine.swift:300-301
let confidence = max(renderedPedal, 0.05)
renderedRPM += rpmRate × dt × confidence
```

- **Pedal pressed (0.8):** confidence = 0.8. Full extrapolation. The RPM is
  genuinely climbing, predict aggressively.
- **Pedal released (0.0):** confidence = 0.05. Almost no extrapolation. RPM
  is coasting/dropping, don't predict overshoot.

This one multiplication elegantly couples the two otherwise independent
systems.

### Drift correction — the safety net

Extrapolation can accumulate small errors over time. A gentle EMA pulls
`renderedRPM` back toward the latest real OBD value:

```swift
// EngineAudioEngine.swift:304
renderedRPM += (targetRPM - renderedRPM) × 0.02
```

At alpha = 0.02, this is very slow — it won't fight the extrapolation on
short timescales, but over a few seconds it prevents drift.

---

## Level 9 — Putting It All Together

### The complete RPM-to-audio pipeline

```
┌─────────────────────────────────────────────────────────────┐
│                     OBD Adapter (14 Hz)                     │
│                    sends raw RPM + pedal                    │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│              EngineAudioEngine.update()                      │
│                                                             │
│  1. Compute velocity:                                       │
│     velocity = (newRPM - oldRPM) / dt                       │
│                                                             │
│  2. Smooth velocity (EMA, α=0.06):                          │
│     targetRate = targetRate×0.5 + instantRate×0.5           │
│                                                             │
│  3. Store targets: targetRPM, targetPedal                   │
└──────────────────────────┬──────────────────────────────────┘
                           │
                  ─── 60 Hz Display Link ───
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│              displayLinkFired() — RPM smoothing              │
│                                                             │
│  4. Smooth rate (EMA, α=0.06):                              │
│     rpmRate += (targetRate - rpmRate) × 0.06                │
│                                                             │
│  5. Extrapolate with confidence:                            │
│     renderedRPM += rpmRate × dt × max(pedal, 0.05)         │
│                                                             │
│  6. Drift correction (EMA, α=0.02):                         │
│     renderedRPM += (targetRPM - renderedRPM) × 0.02        │
│                                                             │
│  7. Output smoothing (EMA, α=0.12):                         │
│     outputRPM += (renderedRPM - outputRPM) × 0.12          │
│                                                             │
│  8. Pedal smoothing (EMA, α=0.15):                          │
│     renderedPedal += (targetPedal - renderedPedal) × 0.15   │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│            RPMAudioMapper.calculateLayerParams()              │
│                                                             │
│  9. Normalize RPM to 0–1:                                   │
│     t = (rpm - lowAnchor) / (highAnchor - lowAnchor)        │
│                                                             │
│ 10. RPM crossfade (equal-power):                            │
│     lowGain  = cos(t × π/2)                                 │
│     highGain = sin(t × π/2)                                 │
│                                                             │
│ 11. Pedal crossfade (equal-power):                          │
│     onGain  = sin(pedal × π/2)                              │
│     offGain = cos(pedal × π/2)                              │
│                                                             │
│ 12. Combine into 2×2 gain matrix:                           │
│     onLow=on×low  onHigh=on×high                            │
│     offLow=off×low offHigh=off×high                         │
│                                                             │
│ 13. Playback rate (pitch):                                  │
│     rate = outputRPM / anchorRPM                            │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                   AVAudioEngine hardware                     │
│                                                             │
│  For each layer:                                            │
│    playerNode.volume    = gain      (how loud)              │
│    varispeedNode.rate   = rate      (how fast = pitch)      │
│                                                             │
│  All layers mix together → speaker output                   │
└─────────────────────────────────────────────────────────────┘
```

### Worked example: Ferrari 458 at 6600 RPM, 80% pedal

**Step 1 — Normalize RPM:**
```
lowAnchor  = 5300  (average of onLow/offLow layers)
highAnchor = 7900  (average of onHigh/offHigh layers)
t = (6600 - 5300) / (7900 - 5300) = 1300/2600 = 0.50
```

**Step 2 — RPM crossfade:**
```
lowGain  = cos(0.50 × π/2) = cos(45°) = 0.707
highGain = sin(0.50 × π/2) = sin(45°) = 0.707
power check: 0.707² + 0.707² = 0.50 + 0.50 = 1.00  ✓
```

**Step 3 — Pedal crossfade:**
```
onGain  = sin(0.80 × π/2) = sin(72°) = 0.951
offGain = cos(0.80 × π/2) = cos(72°) = 0.309
power check: 0.951² + 0.309² = 0.904 + 0.095 = 1.00  ✓
```

**Step 4 — Layer gains:**
```
onLow   = 0.951 × 0.707 = 0.672
onHigh  = 0.951 × 0.707 = 0.672
offLow  = 0.309 × 0.707 = 0.218
offHigh = 0.309 × 0.707 = 0.218
```

**Step 5 — Playback rates:**
```
on_high (anchor 7900):   rate = 6600 / 7900 = 0.835×  (slightly lower pitch)
power_2 (anchor 5300):   rate = 6600 / 5300 = 1.245×  (slightly higher pitch)
off_higher (anchor 7900): rate = 6600 / 7900 = 0.835×
off_midhigh (anchor 5300): rate = 6600 / 5300 = 1.245×
```

**Result:** The listener hears four samples playing simultaneously — the two
"on-throttle" layers are loudest (0.672 each) at slightly different pitches,
creating the rich, layered Ferrari sound. The "off-throttle" layers add a
subtle undertone (0.218 each). All at constant total energy.

---

## Appendix A — Quick Reference Card

```
┌────────────────────────────────────────────────────┐
│              MATH CHEAT SHEET                      │
├────────────────────────────────────────────────────┤
│                                                    │
│  Normalize:   t = (val - min) / (max - min)        │
│  Clamp:       clamp(x, lo, hi) = min(max(x,lo),hi)│
│  Radians:     rad = deg × π / 180                  │
│  Position:    x = cos(rad) × r,  y = sin(rad) × r │
│  Identity:    sin²(θ) + cos²(θ) = 1               │
│  Crossfade:   a = cos(t×π/2),  b = sin(t×π/2)     │
│  EMA:         s = s + α × (target - s)             │
│  Velocity:    v = Δposition / Δtime                │
│  Extrapolate: pos += velocity × dt                 │
│  Pitch:       rate = currentRPM / anchorRPM        │
│                                                    │
│  Settle time ≈ 3 / (α × fps) seconds              │
│                                                    │
└────────────────────────────────────────────────────┘
```

---

## Appendix B — Code ↔ Math Map

| Math Concept          | Code File                    | Lines   | Formula in Code                                    |
|-----------------------|------------------------------|---------|----------------------------------------------------|
| Normalize RPM         | `RPMAudioMapper.swift`       | 28      | `(rpm - lowAnchorRPM) / (highAnchorRPM - ...)`    |
| RPM crossfade (cos)   | `RPMAudioMapper.swift`       | 29      | `cos(t * .pi / 2)`                                |
| RPM crossfade (sin)   | `RPMAudioMapper.swift`       | 30      | `sin(t * .pi / 2)`                                |
| Pedal crossfade       | `RPMAudioMapper.swift`       | 38-39   | `sin(pedalClamped * .pi / 2)`                      |
| 2D gain matrix        | `RPMAudioMapper.swift`       | 42-45   | `onGain * lowGain`, etc.                           |
| Playback rate         | `RPMAudioMapper.swift`       | 84      | `rpm / anchorRPM`                                  |
| Limiter ratio         | `RPMAudioMapper.swift`       | 49-53   | `(rpm - softStart) / (limiter - softStart)`        |
| Clamp                 | `RPMAudioMapper.swift`       | 122-124 | `min(max(value, lo), hi)`                          |
| Velocity              | `EngineAudioEngine.swift`    | 171     | `(rpm - prevOBDRpm) / dt`                          |
| Rate EMA              | `EngineAudioEngine.swift`    | 295     | `rpmRate += (targetRate - rpmRate) * 0.06`         |
| Extrapolation         | `EngineAudioEngine.swift`    | 301     | `renderedRPM += rpmRate * dt * confidence`         |
| Drift correction      | `EngineAudioEngine.swift`    | 304     | `renderedRPM += (targetRPM - renderedRPM) * 0.02` |
| Output EMA            | `EngineAudioEngine.swift`    | 310     | `outputRPM += (renderedRPM - outputRPM) * 0.12`   |
| Pedal EMA             | `EngineAudioEngine.swift`    | 317     | `renderedPedal += (target - rendered) * 0.15`      |
| Gauge normalization   | `RPMGaugeView.swift`         | 12      | `rpm.clamped(to: 0...maxRPM) / maxRPM`            |
| Needle angle          | `RPMGaugeView.swift`         | 16      | `startAngle + sweepAngle * normalizedRPM`          |
| Tick position (x,y)   | `RPMGaugeView.swift`         | 131-142 | `cos(rad) * radius`, `sin(rad) * radius`           |
| Proportional zones    | `RPMGaugeView.swift`         | 32-46   | `0.4`, `0.7`, `0.88`, `1.0`                       |
