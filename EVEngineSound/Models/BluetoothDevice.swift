import Foundation
import CoreBluetooth

/// Represents a discovered BLE OBD adapter
struct BluetoothDevice: Identifiable, Equatable {
    let id: UUID
    let name: String
    var rssi: Int
    let peripheral: CBPeripheral

    /// Known OBD adapter name patterns
    static let obdNamePatterns = ["OBD", "ELM", "V-LINK", "iOBD", "OBDII", "Vgate"]

    /// Whether this device's name matches known OBD adapter patterns
    var isLikelyOBDAdapter: Bool {
        let uppercased = name.uppercased()
        return Self.obdNamePatterns.contains { uppercased.contains($0.uppercased()) }
    }

    static func == (lhs: BluetoothDevice, rhs: BluetoothDevice) -> Bool {
        lhs.id == rhs.id
    }
}
