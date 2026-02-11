# EVEngineSound — Feature & Bug Tracker

## Features (Implemented)

- [x] F-001 BLE/OBD connection with auto-discovery of ELM327 adapters (FFE0, FFF0 service UUIDs)
- [x] F-002 Auto-reconnect on BLE disconnection with peripheral ID tracking
- [x] F-003 ELM327 protocol handler with init sequence, hex parsing, and ISO-TP multi-frame support
- [x] F-004 OBD-II RPM & pedal polling at ~14 Hz (alternating RPM/pedal)
- [x] F-005 Hyundai VMCU BMS mode (UDS DID 0x0101 — RPM offset 53-54, pedal offset 14)
- [x] F-006 Standard OBD-II mode (PID 0x0C for RPM, PID 0x49 for pedal)
- [x] F-007 AVAudioEngine multi-layer audio with 60 Hz CADisplayLink render loop
- [x] F-008 Equal-power cosine crossfade on RPM axis + throttle axis (RPMAudioMapper)
- [x] F-009 Three-stage RPM smoothing (velocity extrapolation, OBD correction, output smoothing)
- [x] F-010 Audio session interruption handling (phone calls, alarms) and route change detection
- [x] F-011 WAV file loading with format conversion (44100 Hz, Float32, mono) and caching
- [x] F-012 Ferrari 458 audio profile (7 layers, RPM 800–9000)
- [x] F-013 Procar audio profile (5 layers, RPM 800–9000)
- [x] F-014 BAC Mono audio profile (9 layers, RPM 800–8500)
- [x] F-015 Dynamic audio profile switching without full engine teardown
- [x] F-016 Dashboard tab — real-time RPM/pedal gauges, connection status, active profile
- [x] F-017 Demo tab — manual RPM/pedal sliders with auto-rev (80 RPM/s up, 48 RPM/s down)
- [x] F-018 Connection tab — BLE scanning, device list with RSSI signal bars
- [x] F-019 Settings tab — profile selection, master volume slider, app version info
- [x] F-020 Debug tab — real-time log viewer with color-coded TX/RX/parsed/error entries
- [x] F-021 RPMGaugeView — circular 270° arc gauge with colored segments and animated needle
- [x] F-022 OBDLogger — timestamped log entries (max 500), direction-coded
- [x] F-023 Use real pedal data in audio engine (currently hardcoded to 100% throttle)
- [x] F-024 PIDParser unified wrappers — `parseRPM(from:isUDS:)` and `parsePedalPosition(from:)`

## Features (Planned / Incomplete)

- [ ] F-025 Auto connect bluetooth
- [ ] F-026 UDS multi-frame reassembly active usage (logic exists but not called by OBDService)
- [ ] F-027 Audio file validation on startup (currently silent failure if files missing)
- [ ] F-028 Share single EngineAudioEngine between Demo and Dashboard (currently independent instances)


## Bugs

- [ ] B-001 ELM327 init sequence mismatch — code sends 5 commands (ATZ, ATE0, ATL0, ATSP6, ATSH7E4), tests expect 6 (ATZ, ATE0, ATL0, ATS0, ATH1, ATSP0)
- [ ] B-006 Demo mode memory leak risk — DemoViewModel creates its own EngineAudioEngine; if view destruction doesn't trigger `stop()`, engine persists
