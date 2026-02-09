# F-023: Use Real Pedal Data + Intent-Aware RPM Smoothing

## Problem

Two related issues:

1. **Hardcoded throttle**: `EngineAudioEngine.update()` set `renderedPedal = 1.0` (always 100%), making the throttle-axis crossfade in `RPMAudioMapper` always behave as full-throttle. Off-throttle engine sound layers (`offLow`, `offHigh`) never played.

2. **RPM overshoot on direction change**: When the driver lifts off the accelerator, the RPM extrapolation system continues projecting upward for ~70ms until the next OBD reading arrives. At +7000 RPM/sec extrapolation, this causes ~500 RPM overshoot — an audible "hump" before the correction kicks in. (Documented in `RPM_SMOOTHING.md`)

## Solution

### Accelerator Pedal Data (Bytes 10-11 from 7E4 220101)

The Hyundai/Kia BMS ECU (7E4) DID 0x0101 response contains pedal position as a **16-bit wrapping counter** at data bytes 10-11:

```
Response: 62 01 01 [data byte 0] [data byte 1] ... [data byte 10] [data byte 11] ...
                                                     ^high byte      ^low byte
```

- Byte 11 (low) counts 0-255 as pedal is pressed further
- Byte 10 (high) increments by 1 each time byte 11 wraps past 255
- Raw value = `byte10 * 256 + byte11`
- Normalized to 0.0-1.0 by dividing by 1023 (10-bit pedal sensor range)

Example readings:
| Pedal press | Byte 10 | Byte 11 | Raw | Percentage |
|-------------|---------|---------|-----|------------|
| None        | 0       | 0       | 0   | 0%         |
| Slight      | 0       | 120     | 120 | ~11.7%     |
| Medium      | 2       | 0       | 512 | ~50%       |
| Full        | 3       | 255     | 1023| 100%       |

### Unified OBD Polling

Previously, `OBDService` alternated between separate RPM and pedal polls — but both used the same `220101` command to the same ECU. Now it parses **both RPM and pedal from a single response**, doubling the effective update rate from ~7Hz per value to ~14Hz for both.

### Intent-Aware RPM Smoothing (No Brake Pedal Needed)

The accelerator pedal is a **leading indicator** — it changes BEFORE RPM does. We classify driver intent from pedal position alone:

```
DriverIntent:
  .accelerating  — pedal > 10%:  confident velocity extrapolation
  .coasting      — pedal <= 10%: stop extrapolation, track actual RPM
```

On transition from `.accelerating` to `.coasting`:
1. Kill any positive (upward) `targetRate` — prevents projecting RPM higher
2. Dampen `rpmRate` by 70% — stops the inertia of the smoothing system
3. Boost drift correction 4x (0.08 vs 0.02) — snaps to actual RPM faster

Additionally, **pedal-modulated extrapolation confidence** scales the extrapolation step by pedal position every frame:
```
renderedRPM += rpmRate * dt * max(pedalPosition, 0.05)
```
- Full pedal → full extrapolation (smooth acceleration)
- Zero pedal → almost no extrapolation (just track OBD)

### Expected Improvement

| Scenario | Before | After |
|----------|--------|-------|
| Steady acceleration | Smooth | Smooth (same) |
| Gas release | Overshoots ~500 RPM | Immediately stops, smooth decay |
| Quick blip (gas-release-gas) | Double overshoot | Clean tracking |
| Coasting | Slight drift | Tracks closely, 4x faster correction |
| Off-throttle sound layers | Never played (hardcoded 100%) | Play correctly based on real pedal |

## Files Changed

| File | Change |
|------|--------|
| `EVEngineSound/Services/OBD/PIDParser.swift` | `parseHyundaiPedal`: byte 14 formula replaced with bytes 10-11 wrapping counter (raw / 1023) |
| `EVEngineSound/Services/OBD/OBDService.swift` | Polling loop uses `pollBMS()` for unified RPM+pedal from single request |
| `EVEngineSound/Services/Audio/EngineAudioEngine.swift` | `DriverIntent` enum, real pedal data, pedal-modulated extrapolation, boosted coasting correction |
| `EVEngineSoundTests/PIDParserTests.swift` | New test cases for byte 10-11 wrapping counter pedal format |

## Tuning Notes

- **`pedalRawMax` (1023)**: If testing shows the full-press raw value is different (e.g., 512 or 2047), change `PIDParser.pedalRawMax`
- **`coastingThreshold` (0.10)**: If pedal noise triggers false coasting, raise to 0.15
- **`coastingCorrectionFactor` (0.08)**: Higher = snappier tracking when coasting, lower = smoother but may drift
- **`rpmRate` damping (0.3)**: On coast transition, rpmRate is multiplied by 0.3. Lower = more aggressive stop, higher = more gradual
