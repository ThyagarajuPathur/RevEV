import Foundation
import Combine

/// Central dependency container that owns all services and wires them together.
final class AppCoordinator: ObservableObject {

    // MARK: - Services

    let bluetoothManager: BluetoothManager
    let obdService: OBDService
    let audioEngine: EngineAudioEngine
    let obdLogger: OBDLogger

    // MARK: - Published State

    @Published var currentProfile: AudioProfile = .ferrari458
    @Published private(set) var bleState: BluetoothManager.ConnectionState = .disconnected

    // MARK: - Private

    private var cancellables = Set<AnyCancellable>()
    private var didAttemptAutoConnect = false

    // MARK: - Init

    init() {
        UserDefaults.standard.register(defaults: ["autoConnectEnabled": true])

        let btManager = BluetoothManager()
        let logger = OBDLogger()
        let obd = OBDService(bluetoothManager: btManager, logger: logger)
        let engine = EngineAudioEngine()

        self.bluetoothManager = btManager
        self.obdLogger = logger
        self.obdService = obd
        self.audioEngine = engine

        // Give BLE manager and audio engine access to the logger
        btManager.logger = logger
        engine.logger = logger

        // Wire OBD vehicle data to audio engine
        obd.vehicleDataPublisher
            .receive(on: DispatchQueue.main)
            .sink { [weak engine] data in
                engine?.update(rpm: data.rpm, pedalPosition: data.pedalPosition)
            }
            .store(in: &cancellables)

        // Sync profile changes to audio engine
        $currentProfile
            .dropFirst()
            .sink { [weak engine] profile in
                engine?.switchProfile(to: profile)
            }
            .store(in: &cancellables)

        // Mirror BLE state to our own @Published (so views can observe it reactively)
        btManager.$connectionState
            .receive(on: DispatchQueue.main)
            .assign(to: &$bleState)

        // Auto-connect on first BLE power-on
        btManager.$centralState
            .first(where: { $0 == .poweredOn })
            .sink { [weak self] _ in
                guard let self, !self.didAttemptAutoConnect else { return }
                self.didAttemptAutoConnect = true
                if UserDefaults.standard.bool(forKey: "autoConnectEnabled") {
                    self.obdLogger.logParsed("Auto-connect: attempting…")
                    self.bluetoothManager.autoConnect()
                }
            }
            .store(in: &cancellables)

        // Log every BLE state change & auto-start/stop OBD service
        btManager.$connectionState
            .removeDuplicates()
            .sink { [weak self] state in
                guard let self else { return }
                self.obdLogger.logParsed("BLE state → \(state)")
                switch state {
                case .ready:
                    self.obdLogger.logParsed("BLE ready — starting OBD service…")
                    Task { [weak self] in
                        do {
                            try await self?.obdService.start()
                            self?.obdLogger.logParsed("OBD service started — polling active")
                        } catch {
                            self?.obdLogger.logError("OBD start failed: \(error.localizedDescription)")
                        }
                    }
                case .disconnected:
                    self.obdService.stop()
                default:
                    break
                }
            }
            .store(in: &cancellables)

        logger.logParsed("AppCoordinator initialized")
    }

    // MARK: - Actions

    func start() {
        audioEngine.start(with: currentProfile)
        obdLogger.logParsed("Audio engine started with profile: \(currentProfile.name)")
    }

    func stop() {
        audioEngine.stop()
    }

    func selectProfile(_ profile: AudioProfile) {
        currentProfile = profile
    }
}
