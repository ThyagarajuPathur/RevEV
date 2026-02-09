import Foundation
import Combine

/// Main OBD service that polls vehicle data via BLE/ELM327 and publishes VehicleData
final class OBDService: VehicleDataProvider, ObservableObject {

    // MARK: - VehicleDataProvider

    var vehicleDataPublisher: AnyPublisher<VehicleData, Never> {
        vehicleDataSubject.eraseToAnyPublisher()
    }

    var currentData: VehicleData {
        _currentData
    }

    // MARK: - Published

    @Published private(set) var isPolling = false

    // MARK: - Private Properties

    private let bluetoothManager: BluetoothManager
    private let adapter: ELM327Adapter
    private let logger: OBDLogger?
    private let vehicleDataSubject = CurrentValueSubject<VehicleData, Never>(.zero)
    private var _currentData = VehicleData.zero

    private var pollingTask: Task<Void, Never>?
    private var cancellables = Set<AnyCancellable>()

    /// Latest raw values (smoothing now done in audio engine's display link)
    private var latestRPM: Double = 0
    private var latestPedal: Double = 0

    /// Polling interval in nanoseconds (70ms ≈ 14Hz — matching working v2)
    private let pollingIntervalNanos: UInt64 = 70_000_000

    /// Whether to use Hyundai/Kia EV BMS PIDs or standard OBD-II PIDs
    var useHyundai = true

    // MARK: - Init

    init(bluetoothManager: BluetoothManager, logger: OBDLogger? = nil) {
        self.bluetoothManager = bluetoothManager
        self.logger = logger
        self.adapter = ELM327Adapter(bluetoothManager: bluetoothManager, logger: logger)
        observeConnectionState()
    }

    // MARK: - Public API

    /// Initialize the adapter and start polling
    func start() async throws {
        try await adapter.initialize()
        startPolling()
    }

    /// Stop polling and disconnect
    func stop() {
        stopPolling()
    }

    func startPolling() {
        guard !isPolling else { return }
        isPolling = true
        logger?.logParsed("Polling started (mode: \(useHyundai ? "Hyundai VMCU" : "Standard OBD-II"))")

        // Hyundai mode: both RPM and pedal come from the same 220101 response,
        // so we parse both from a single OBD request via pollBMS().
        // This doubles the effective update rate (14Hz for BOTH values vs 7Hz each).
        //
        // Standard OBD-II mode: alternate between RPM (010C) and pedal (0149) polls.
        pollingTask = Task { [weak self] in
            guard let self else { return }
            var requestRPM = true

            while !Task.isCancelled {
                do {
                    if self.useHyundai {
                        // Unified: single 220101 request gives both RPM and pedal
                        let result = try await self.pollBMS()
                        self.latestRPM = result.rpm
                        self.latestPedal = result.pedal
                        self.logger?.logParsed("RPM: \(Int(result.rpm))  Pedal: \(String(format: "%.1f%%", result.pedal * 100))")
                    } else {
                        // Standard OBD-II: alternate between RPM and pedal PIDs
                        if requestRPM {
                            self.latestRPM = try await self.pollRPM()
                            self.logger?.logParsed("RPM: \(Int(self.latestRPM))")
                        } else {
                            self.latestPedal = try await self.pollPedal()
                        }
                        requestRPM.toggle()
                    }

                    // Publish raw data — audio engine interpolates at 60fps
                    let data = VehicleData(
                        rpm: self.latestRPM,
                        pedalPosition: self.latestPedal,
                        isConnected: true
                    )
                    self._currentData = data
                    self.vehicleDataSubject.send(data)

                } catch is CancellationError {
                    break
                } catch {
                    self.logger?.logError("Poll error: \(error.localizedDescription)")
                }

                try? await Task.sleep(nanoseconds: self.pollingIntervalNanos)
            }
        }
    }

    func stopPolling() {
        pollingTask?.cancel()
        pollingTask = nil
        isPolling = false
        logger?.logParsed("Polling stopped")

        latestRPM = 0
        latestPedal = 0
        _currentData = VehicleData.zero
        vehicleDataSubject.send(.zero)
    }

    // MARK: - Polling

    /// Poll RPM and pedal from a single 220101 request to BMS (7E4).
    /// The response contains both values at different offsets.
    private func pollBMS() async throws -> (rpm: Double, pedal: Double) {
        let bytes = try await adapter.sendAndParseHex(OBDCommand.hyundaiRPMRequest)
        logger?.logParsed("BMS response: \(bytes.count) bytes")
        let rpm = PIDParser.parseHyundaiRPM(from: bytes)
        let pedal = PIDParser.parseHyundaiPedal(from: bytes)
        return (rpm, pedal)
    }

    private func pollRPM() async throws -> Double {
        if useHyundai {
            // Header already set to 7E4 during init; send 220101
            let bytes = try await adapter.sendAndParseHex(OBDCommand.hyundaiRPMRequest)
            logger?.logParsed("RPM response: \(bytes.count) bytes")
            return PIDParser.parseHyundaiRPM(from: bytes)
        } else {
            let bytes = try await adapter.sendAndParseHex(OBDCommand.standardRPM)
            let dataBytes = bytes.count > 2 ? Array(bytes[2...]) : bytes
            return PIDParser.parseStandardRPM(from: dataBytes)
        }
    }

    private func pollPedal() async throws -> Double {
        if useHyundai {
            // Same 220101 command — pedal data is also in this response
            let bytes = try await adapter.sendAndParseHex(OBDCommand.hyundaiRPMRequest)
            return PIDParser.parseHyundaiPedal(from: bytes)
        } else {
            let bytes = try await adapter.sendAndParseHex(OBDCommand.standardPedalPosition)
            let dataBytes = bytes.count > 2 ? Array(bytes[2...]) : bytes
            return PIDParser.parseStandardPedalPosition(from: dataBytes)
        }
    }

    // MARK: - Connection Observation

    private func observeConnectionState() {
        bluetoothManager.connectionStatePublisher
            .sink { [weak self] state in
                guard let self else { return }
                if state == .disconnected {
                    self.stopPolling()
                }
            }
            .store(in: &cancellables)
    }
}
