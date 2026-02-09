import Foundation
import Combine

/// Shared vehicle data model used by OBD service and audio engine
struct VehicleData: Equatable {
    /// Engine RPM (0–9000+)
    var rpm: Double = 0
    /// Accelerator pedal position (0.0–1.0)
    var pedalPosition: Double = 0
    /// Whether data is currently being received
    var isConnected: Bool = false

    static let idle = VehicleData(rpm: 800, pedalPosition: 0, isConnected: true)
    static let zero = VehicleData()
}

/// Protocol for anything that produces vehicle data (OBD or Demo)
protocol VehicleDataProvider: AnyObject {
    var vehicleDataPublisher: AnyPublisher<VehicleData, Never> { get }
    var currentData: VehicleData { get }
}
