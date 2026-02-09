# Changelog — Dashboard Connection Status, RPM/Pedal Updates + Debug View

Date: 2026-02-09

---

## Modified Files

### `EVEngineSound/App/AppCoordinator.swift`
1. **Added `obdLogger: OBDLogger`** property — centralized debug logger owned by coordinator
2. **Added `@Published bleState`** — mirrors `bluetoothManager.connectionState` as a reactive `@Published` property so SwiftUI views can observe BLE state changes
3. **Created `OBDLogger` instance in `init()`** and passes it to `OBDService`
4. **Set `btManager.logger = logger`** — wires logger into BluetoothManager for service/characteristic discovery logging
5. **Added BLE state subscription** — subscribes to `btManager.$connectionState` with `removeDuplicates()`:
   - On `.ready` → calls `obdService.start()` in a Task (auto-starts OBD polling)
   - On `.disconnected` → calls `obdService.stop()` (auto-stops polling)
   - Logs every state transition
6. **Added startup log** — `"AppCoordinator initialized"` on init, `"Audio engine started"` in `start()`

### `EVEngineSound/ViewModels/DashboardViewModel.swift`
1. **Added `cancellables.removeAll()`** at start of `bind(to:)` — prevents duplicate subscriptions on re-appear
2. **Changed `isConnected` source** — was driven by `VehicleData.isConnected` (only true during OBD polling), now subscribes to `coordinator.$bleState` and sets `isConnected = (state == .connected || state == .ready)` so dashboard shows "Connected" as soon as BLE connects
3. **Removed `isConnected` from OBD data sink** — RPM/pedal subscription no longer sets `isConnected`

### `EVEngineSound/Services/Bluetooth/BluetoothManager.swift`
1. **Added `var logger: OBDLogger?`** — optional logger set by AppCoordinator
2. **Added `elmServiceUUIDs` set** — `[FFE0, FFF0]` used to prioritize ELM327 service characteristics
3. **Added `writeFromELMService`, `notifyFromELMService` flags** — track whether selected characteristics came from a known ELM327 service
4. **Added `pendingServiceCount`** — tracks how many services still need characteristic discovery
5. **Changed `discoverServices(Self.serviceUUIDs)` → `discoverServices(nil)`** — discovers ALL services instead of only FFE0/FFF0, fixing adapters with non-standard UUIDs
6. **Changed `discoverCharacteristics(Self.characteristicUUIDs, ...)` → `discoverCharacteristics(nil, ...)`** — discovers ALL characteristics per service
7. **Rewrote characteristic selection logic** — ELM service characteristics override previously selected standard BLE ones (fixes wrong char selection where Tx Power `2A07` was chosen over ELM327 `FFF2`)
8. **Added ready-state fallback** — if no known ELM service found after all services processed, uses best available characteristics
9. **Added comprehensive logging** in all delegate methods — logs every service UUID, every characteristic with properties, selection decisions, errors
10. **Removed hardcoded `serviceUUIDs` and `characteristicUUIDs` arrays** — no longer needed since discovery is unfiltered
11. **Reset new tracking properties in `cleanupConnection()`**

### `EVEngineSound/Services/Bluetooth/ELM327Adapter.swift`
1. **Added `logger: OBDLogger?` parameter** to `init()` — accepts optional logger
2. **Added `commandToken: UInt64`** — unique token per command to fix timeout race condition
3. **Fixed timeout race condition** — each `sendCommand` captures `myToken`; timeout closure checks `self.commandToken == myToken` before firing, so stale timeouts from previous commands can't kill current command's continuation
4. **Added token advancement in response handler** — `subscribeToResponses` advances `commandToken` when response arrives, invalidating any pending timeout
5. **Added `"STOPPED"` to error responses** — was silently parsed as empty hex, now throws `OBDError.noResponse`
6. **Added `"STOPPED"` to `parseHexResponse` skip list** — lines starting with "STOPPED" are filtered out during hex parsing
7. **Logs every sent command** (`logSent`), **every received response** (`logReceived`), **parsed hex bytes** (`logParsed`), and **errors** (`logError`)
8. **Removed unconditional `ATSH7E0`** from `initialize()` — was overriding default 7DF broadcast address, causing NO DATA on vehicles that only respond to functional addressing

### `EVEngineSound/Services/OBD/OBDService.swift`
1. **Added `logger: OBDLogger?` parameter** to `init()` — passes logger to ELM327Adapter
2. **Renamed `useUDS` → `useHyundai`**, default changed from `false` to `true` — for Hyundai/Kia VMCU PIDs
3. **Changed polling interval** from `100_000_000` (100ms) to `500_000_000` (500ms) — ELM327 needs time between commands
4. **Moved `requestRPM.toggle()` outside the do/catch block** — now alternates between RPM and pedal even when errors occur
5. **Rewrote `pollRPM()` for Hyundai mode** — sends `ATSH7E3` to switch to VMCU RPM ECU, then sends `2102` (service 0x21 PID 0x02), parses with `PIDParser.parseHyundaiRPM`
6. **Rewrote `pollPedal()` for Hyundai mode** — sends `ATSH7E2` to switch to VMCU Pedal ECU, then sends `2101` (service 0x21 PID 0x01), parses with `PIDParser.parseHyundaiPedal`
7. **Logs polling mode** on start (`"Hyundai VMCU"` or `"Standard OBD-II"`)
8. **Logs parsed values and errors** throughout polling loop

### `EVEngineSound/Services/OBD/PIDParser.swift`
1. **Rewrote `parseHyundaiRPM`** — was generic signed 16-bit MSB, now parses VMCU 0x21/0x02 response: `RPM = (Signed(bytes[7]) * 256) + bytes[6]` (positions E and F in the response after 61 02 header)
2. **Added `parseHyundaiPedal`** — new method for VMCU 0x21/0x01 response: `Pedal = bytes[14] / 2` (position M in response), normalized to 0.0–1.0
3. **Removed unused methods** — `parsePedalPosition` and `parseRPM` generic wrappers removed

### `EVEngineSound/Models/OBDCommand.swift`
1. **Added `headersOff = "ATH0"`** — for standard OBD-II mode (clean responses)
2. **Changed `initSequence`** to use `headersOff` instead of `headersOn` — cleaner response parsing
3. **Added `headerVMCU_RPM = "ATSH7E3"`** — ECU header for Hyundai VMCU RPM
4. **Added `headerVMCU_Pedal = "ATSH7E2"`** — ECU header for Hyundai VMCU pedal
5. **Changed `hyundaiRPMRequest`** from `udsReadRequest(did: 0x0101)` ("220101") to `"2102"` — service 0x21 PID 0x02
6. **Changed `hyundaiPedalRequest`** from `udsReadRequest(did: 0x0154)` ("220154") to `"2101"` — service 0x21 PID 0x01
7. **Added `kwpPositiveResponse: UInt8 = 0x61`** — KWP2000 service 0x21 positive response byte

### `EVEngineSound/App/EVEngineSoundApp.swift`
1. **Added Debug tab** — `DebugView(logger: coordinator.obdLogger)` with `Label("Debug", systemImage: "terminal")`, placed between Connection and Settings tabs

### `EVEngineSound.xcodeproj/project.pbxproj`
1. **Added `OBDLogger.swift` file reference** and build file entry to EVEngineSound target Sources phase
2. **Added `DebugView.swift` file reference** and build file entry to EVEngineSound target Sources phase
3. **Added both files to EVEngineSound PBXGroup** children list

---

## New Files

### `EVEngineSound/Services/OBD/OBDLogger.swift`
- `OBDLogger` class (`ObservableObject`) with `@Published entries: [LogEntry]`
- `LogEntry` struct: `id` (UUID), `timestamp` (Date), `direction` (sent/received/parsed/error), `message` (String)
- Methods: `logSent`, `logReceived`, `logParsed`, `logError`, `clear`
- Thread-safe: dispatches to main thread if called from background
- Capped at 500 entries to prevent memory growth

### `EVEngineSound/Views/Debug/DebugView.swift`
- Terminal-style scrolling log view with monospaced text
- Color-coded entries: blue (TX), green (RX), orange (parsed), red (error)
- Timestamp prefix (`HH:mm:ss.SSS`) on each line
- Connection state banner at top driven by `coordinator.$bleState`
- Entry count display
- Auto-scroll to bottom on new entries
- Clear button in toolbar
- Empty-state message when no log entries

---

## Bugs Fixed

### 1. Dashboard never showed "Connected"
**Root cause:** `isConnected` was driven by `VehicleData.isConnected` from OBD polling, which never started because `obdService.start()` was never called.
**Fix:** `isConnected` now driven by BLE `connectionState` (`.connected` or `.ready`). OBD service auto-starts when BLE reaches `.ready`.

### 2. RPM and pedal never updated
**Root cause (a):** `obdService.start()` was never called — no link between BLE connection and OBD polling.
**Root cause (b):** Wrong OBD commands — standard PIDs `010C`/`0149` don't work on Hyundai/Kia EVs that use VMCU service 0x21.
**Root cause (c):** `ATSH7E0` overrode the default broadcast address, and `ATH1` added CAN headers the parser didn't handle.
**Fix:** Auto-start OBD on BLE ready. Use Hyundai VMCU commands (`2102` to ECU 7E3, `2101` to ECU 7E2). Headers off, no preset ATSH.

### 3. BLE stuck at "connected", never reached "ready"
**Root cause:** `discoverServices` filtered to only `FFE0`/`FFF0` UUIDs. User's adapter has service `FFF0` but also standard BLE services. Characteristics from wrong services (Tx Power `2A07`, Battery `2A19`) were selected over ELM327 ones (`FFF2`, `FFF1`).
**Fix:** Discover all services/characteristics. Prioritize known ELM327 service UUIDs. Allow override when ELM service chars are found after standard BLE ones.

### 4. Timeout race condition in ELM327Adapter
**Root cause:** `DispatchQueue.main.asyncAfter` timeout from command A would fire and kill command B's continuation, since the timeout wasn't invalidated when a response arrived.
**Fix:** Command token system — each command gets a unique token; timeouts check `commandToken == myToken` before firing; response handler advances the token.

### 5. "STOPPED" response silently parsed as 0
**Root cause:** "STOPPED" wasn't in the error response list, so `sendAndParseHex` parsed empty hex bytes as valid data (0 RPM, 0% pedal).
**Fix:** Added "STOPPED" to error responses, throws `OBDError.noResponse`.

### 6. Polling only tried RPM on errors
**Root cause:** `requestRPM.toggle()` was inside the `do` block, so on error it never toggled.
**Fix:** Moved toggle outside do/catch — always alternates between RPM and pedal.
