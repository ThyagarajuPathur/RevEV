import Foundation
import Combine

/// ViewModel for the Bluetooth connection UI
final class ConnectionViewModel: ObservableObject {

    // MARK: - Published Properties

    @Published private(set) var devices: [BluetoothDevice] = []
    @Published private(set) var connectionState: BluetoothManager.ConnectionState = .disconnected
    @Published private(set) var isAutoConnecting = false
    @Published var errorMessage: String?

    // MARK: - Private

    private let bluetoothManager: BluetoothManager
    private var cancellables = Set<AnyCancellable>()

    var isScanning: Bool { connectionState == .scanning }
    var isConnected: Bool {
        connectionState == .connected || connectionState == .ready
    }

    // MARK: - Init

    init(bluetoothManager: BluetoothManager) {
        self.bluetoothManager = bluetoothManager
        bind()
    }

    // MARK: - Public API

    func startScanning() {
        errorMessage = nil
        bluetoothManager.startScanning()
    }

    func stopScanning() {
        bluetoothManager.stopScanning()
    }

    func connect(to device: BluetoothDevice) {
        errorMessage = nil
        bluetoothManager.connect(to: device)
    }

    func disconnect() {
        bluetoothManager.disconnect()
    }

    // MARK: - Binding

    private func bind() {
        bluetoothManager.$discoveredDevices
            .receive(on: DispatchQueue.main)
            .assign(to: &$devices)

        bluetoothManager.$connectionState
            .receive(on: DispatchQueue.main)
            .assign(to: &$connectionState)

        bluetoothManager.$isAutoConnecting
            .receive(on: DispatchQueue.main)
            .assign(to: &$isAutoConnecting)

        bluetoothManager.$error
            .compactMap { $0?.errorDescription }
            .receive(on: DispatchQueue.main)
            .assign(to: &$errorMessage)
    }
}
