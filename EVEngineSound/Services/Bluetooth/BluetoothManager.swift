import Foundation
import CoreBluetooth
import Combine

/// Manages CoreBluetooth scanning, connection, and data exchange with BLE OBD adapters
final class BluetoothManager: NSObject, ObservableObject {

    // MARK: - Connection State

    enum ConnectionState: Equatable {
        case disconnected
        case scanning
        case connecting
        case connected
        case ready
    }

    // MARK: - Known ELM327 Service UUIDs

    private static let elmServiceUUIDs: Set<CBUUID> = [
        CBUUID(string: "FFE0"),
        CBUUID(string: "FFF0")
    ]

    // MARK: - UserDefaults Keys

    static let lastOBDDeviceUUIDKey = "lastOBDDeviceUUID"
    static let lastOBDDeviceNameKey = "lastOBDDeviceName"

    // MARK: - Published Properties

    @Published private(set) var connectionState: ConnectionState = .disconnected
    @Published private(set) var discoveredDevices: [BluetoothDevice] = []
    @Published private(set) var error: OBDError?
    @Published private(set) var isAutoConnecting = false
    @Published private(set) var centralState: CBManagerState = .unknown

    // MARK: - Combine Publishers

    /// Publishes raw string data received from the adapter
    var receivedDataPublisher: AnyPublisher<String, Never> {
        receivedDataSubject.eraseToAnyPublisher()
    }

    var connectionStatePublisher: AnyPublisher<ConnectionState, Never> {
        $connectionState.eraseToAnyPublisher()
    }

    // MARK: - Logger

    var logger: OBDLogger?

    // MARK: - Private Properties

    private var centralManager: CBCentralManager!
    private var connectedPeripheral: CBPeripheral?
    private var writeCharacteristic: CBCharacteristic?
    private var notifyCharacteristic: CBCharacteristic?
    private var writeFromELMService = false
    private var notifyFromELMService = false
    private var pendingServiceCount = 0

    private let receivedDataSubject = PassthroughSubject<String, Never>()
    private var responseBuffer = ""

    private var shouldAutoReconnect = false
    private var lastConnectedPeripheralID: UUID?

    private let scanTimeout: TimeInterval = 30
    private var scanTimer: Timer?

    // MARK: - Init

    override init() {
        super.init()
        centralManager = CBCentralManager(delegate: self, queue: .main)
    }

    // MARK: - Public API

    func startScanning() {
        guard centralManager.state == .poweredOn else {
            if centralManager.state == .poweredOff {
                error = .bluetoothOff
            } else if centralManager.state == .unauthorized {
                error = .bluetoothUnauthorized
            }
            return
        }
        discoveredDevices.removeAll()
        connectionState = .scanning
        centralManager.scanForPeripherals(withServices: nil, options: [
            CBCentralManagerScanOptionAllowDuplicatesKey: false
        ])

        scanTimer?.invalidate()
        scanTimer = Timer.scheduledTimer(withTimeInterval: scanTimeout, repeats: false) { [weak self] _ in
            self?.stopScanning()
        }
    }

    func stopScanning() {
        scanTimer?.invalidate()
        scanTimer = nil
        if centralManager.isScanning {
            centralManager.stopScan()
        }
        if connectionState == .scanning {
            connectionState = .disconnected
        }
    }

    func connect(to device: BluetoothDevice) {
        stopScanning()
        isAutoConnecting = false
        connectionState = .connecting
        shouldAutoReconnect = true
        lastConnectedPeripheralID = device.peripheral.identifier
        connectedPeripheral = device.peripheral
        device.peripheral.delegate = self
        centralManager.connect(device.peripheral, options: nil)

        // Persist for auto-connect on next launch
        UserDefaults.standard.set(device.peripheral.identifier.uuidString, forKey: Self.lastOBDDeviceUUIDKey)
        UserDefaults.standard.set(device.name, forKey: Self.lastOBDDeviceNameKey)
    }

    /// Attempts to auto-connect to the last known device using persisted UUID.
    /// Call this after BLE powers on.
    func autoConnect() {
        guard let uuidString = UserDefaults.standard.string(forKey: Self.lastOBDDeviceUUIDKey),
              let uuid = UUID(uuidString: uuidString) else { return }

        let peripherals = centralManager.retrievePeripherals(withIdentifiers: [uuid])
        guard let peripheral = peripherals.first else { return }

        isAutoConnecting = true
        connectionState = .connecting
        shouldAutoReconnect = true
        lastConnectedPeripheralID = uuid
        connectedPeripheral = peripheral
        peripheral.delegate = self
        centralManager.connect(peripheral, options: nil)
    }

    func disconnect() {
        shouldAutoReconnect = false
        if let peripheral = connectedPeripheral {
            centralManager.cancelPeripheralConnection(peripheral)
        }
        cleanupConnection()
    }

    /// Send a raw string command to the adapter (appends \r)
    func send(_ command: String) {
        guard let characteristic = writeCharacteristic,
              let peripheral = connectedPeripheral,
              let data = "\(command)\r".data(using: .ascii) else {
            return
        }

        let writeType: CBCharacteristicWriteType =
            characteristic.properties.contains(.writeWithoutResponse) ? .withoutResponse : .withResponse

        // BLE has a max MTU; chunk if needed (default 20 bytes)
        let mtu = peripheral.maximumWriteValueLength(for: writeType)
        var offset = 0
        while offset < data.count {
            let end = min(offset + mtu, data.count)
            let chunk = data.subdata(in: offset..<end)
            peripheral.writeValue(chunk, for: characteristic, type: writeType)
            offset = end
        }
    }

    // MARK: - Private Helpers

    private func cleanupConnection() {
        connectedPeripheral = nil
        writeCharacteristic = nil
        notifyCharacteristic = nil
        writeFromELMService = false
        notifyFromELMService = false
        pendingServiceCount = 0
        responseBuffer = ""
        isAutoConnecting = false
        connectionState = .disconnected
    }

    private func attemptAutoReconnect() {
        guard shouldAutoReconnect, let id = lastConnectedPeripheralID else { return }
        let peripherals = centralManager.retrievePeripherals(withIdentifiers: [id])
        if let peripheral = peripherals.first {
            connectionState = .connecting
            connectedPeripheral = peripheral
            peripheral.delegate = self
            centralManager.connect(peripheral, options: nil)
        }
    }
}

// MARK: - CBCentralManagerDelegate

extension BluetoothManager: CBCentralManagerDelegate {

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        centralState = central.state
        switch central.state {
        case .poweredOn:
            break
        case .poweredOff:
            error = .bluetoothOff
            cleanupConnection()
        case .unauthorized:
            error = .bluetoothUnauthorized
            cleanupConnection()
        default:
            cleanupConnection()
        }
    }

    func centralManager(_ central: CBCentralManager, didDiscover peripheral: CBPeripheral,
                        advertisementData: [String: Any], rssi RSSI: NSNumber) {
        let name = peripheral.name ?? advertisementData[CBAdvertisementDataLocalNameKey] as? String ?? ""
        guard !name.isEmpty else { return }

        let device = BluetoothDevice(
            id: peripheral.identifier,
            name: name,
            rssi: RSSI.intValue,
            peripheral: peripheral
        )

        // Only show devices matching OBD patterns or update existing entries
        if let index = discoveredDevices.firstIndex(where: { $0.id == device.id }) {
            discoveredDevices[index] = device
        } else if device.isLikelyOBDAdapter {
            discoveredDevices.append(device)
        }
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        isAutoConnecting = false
        connectionState = .connected

        // Ensure the device appears in discoveredDevices (auto-connect
        // bypasses scanning, so the device may not have been discovered).
        if !discoveredDevices.contains(where: { $0.id == peripheral.identifier }) {
            let name = peripheral.name
                ?? UserDefaults.standard.string(forKey: Self.lastOBDDeviceNameKey)
                ?? "OBD Adapter"
            let device = BluetoothDevice(
                id: peripheral.identifier,
                name: name,
                rssi: 0,
                peripheral: peripheral
            )
            discoveredDevices.append(device)
        }

        logger?.logParsed("BLE connected — discovering ALL services…")
        // Discover ALL services (nil) so we don't miss non-standard UUIDs
        peripheral.discoverServices(nil)
    }

    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        logger?.logError("BLE failed to connect: \(error?.localizedDescription ?? "unknown")")
        self.error = .connectionFailed
        cleanupConnection()
    }

    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        if shouldAutoReconnect && error != nil {
            self.error = .connectionLost
            attemptAutoReconnect()
        } else {
            cleanupConnection()
        }
    }
}

// MARK: - CBPeripheralDelegate

extension BluetoothManager: CBPeripheralDelegate {

    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        if let error {
            logger?.logError("Service discovery error: \(error.localizedDescription)")
        }
        guard let services = peripheral.services, !services.isEmpty else {
            logger?.logError("No services found on peripheral!")
            self.error = .connectionFailed
            return
        }
        pendingServiceCount = services.count
        for service in services {
            logger?.logParsed("Found service: \(service.uuid.uuidString)")
            // Discover ALL characteristics (nil) for each service
            peripheral.discoverCharacteristics(nil, for: service)
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        if let error {
            logger?.logError("Characteristic discovery error for \(service.uuid): \(error.localizedDescription)")
        }
        guard let characteristics = service.characteristics else { return }

        let isELMService = Self.elmServiceUUIDs.contains(service.uuid)

        for characteristic in characteristics {
            let props = characteristic.properties
            var propNames: [String] = []
            if props.contains(.read) { propNames.append("read") }
            if props.contains(.write) { propNames.append("write") }
            if props.contains(.writeWithoutResponse) { propNames.append("writeNoResp") }
            if props.contains(.notify) { propNames.append("notify") }
            if props.contains(.indicate) { propNames.append("indicate") }
            logger?.logParsed("  Char \(characteristic.uuid.uuidString) [\(propNames.joined(separator: ", "))]\(isELMService ? " ★ELM" : "")")

            // Select notify: prefer ELM service, otherwise take first available
            if props.contains(.notify) {
                if notifyCharacteristic == nil || (isELMService && !notifyFromELMService) {
                    if let old = notifyCharacteristic {
                        peripheral.setNotifyValue(false, for: old)
                        logger?.logParsed("  → Replacing NOTIFY (was \(old.uuid.uuidString))")
                    }
                    notifyCharacteristic = characteristic
                    notifyFromELMService = isELMService
                    peripheral.setNotifyValue(true, for: characteristic)
                    logger?.logParsed("  → Selected as NOTIFY characteristic")
                }
            }
            // Select write: prefer ELM service, otherwise take first available
            if props.contains(.write) || props.contains(.writeWithoutResponse) {
                if writeCharacteristic == nil || (isELMService && !writeFromELMService) {
                    if let old = writeCharacteristic {
                        logger?.logParsed("  → Replacing WRITE (was \(old.uuid.uuidString))")
                    }
                    writeCharacteristic = characteristic
                    writeFromELMService = isELMService
                    logger?.logParsed("  → Selected as WRITE characteristic")
                }
            }
        }

        pendingServiceCount = max(0, pendingServiceCount - 1)

        if writeCharacteristic != nil && notifyCharacteristic != nil && connectionState != .ready {
            // Go ready immediately if we found ELM service chars
            if writeFromELMService && notifyFromELMService {
                logger?.logParsed("ELM327 Write + Notify found — BLE READY")
                connectionState = .ready
            }
            // Or fall back when all services have been processed
            else if pendingServiceCount == 0 {
                logger?.logParsed("All services processed — using best available chars — BLE READY")
                connectionState = .ready
            }
        } else if pendingServiceCount == 0 && connectionState != .ready {
            logger?.logError("All services processed but missing write or notify characteristic!")
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        guard let data = characteristic.value,
              let text = String(data: data, encoding: .ascii) else { return }

        responseBuffer += text

        // ELM327 uses ">" as the prompt indicating response is complete
        if responseBuffer.contains(">") {
            let response = responseBuffer
                .replacingOccurrences(of: ">", with: "")
                .trimmingCharacters(in: .whitespacesAndNewlines)
            receivedDataSubject.send(response)
            responseBuffer = ""
        }
    }
}
