import Foundation

/// Errors from BLE/OBD communication
enum OBDError: LocalizedError {
    case bluetoothOff
    case bluetoothUnauthorized
    case deviceNotFound
    case connectionFailed
    case connectionLost
    case initializationFailed
    case noResponse
    case invalidResponse
    case timeout
    case unsupportedProtocol

    var errorDescription: String? {
        switch self {
        case .bluetoothOff:
            return "Bluetooth is turned off. Please enable Bluetooth in Settings."
        case .bluetoothUnauthorized:
            return "Bluetooth access is not authorized. Please grant permission in Settings."
        case .deviceNotFound:
            return "No OBD adapter found. Make sure your adapter is powered on."
        case .connectionFailed:
            return "Failed to connect to the OBD adapter."
        case .connectionLost:
            return "Connection to the OBD adapter was lost."
        case .initializationFailed:
            return "Failed to initialize the ELM327 adapter."
        case .noResponse:
            return "No response from the vehicle ECU."
        case .invalidResponse:
            return "Received an invalid response from the adapter."
        case .timeout:
            return "The request timed out."
        case .unsupportedProtocol:
            return "The vehicle protocol is not supported."
        }
    }
}
