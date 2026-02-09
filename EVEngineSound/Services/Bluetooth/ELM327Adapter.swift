import Foundation
import Combine

/// Handles ELM327 initialization, command sending, and response parsing
final class ELM327Adapter {

    // MARK: - Properties

    private let bluetoothManager: BluetoothManager
    private let logger: OBDLogger?
    private var cancellables = Set<AnyCancellable>()
    private var responseContinuation: CheckedContinuation<String, Error>?
    private var commandToken: UInt64 = 0
    private let responseTimeout: TimeInterval = 5.0

    /// Whether the adapter has been initialized successfully
    private(set) var isInitialized = false

    // MARK: - Known Error Responses

    private static let errorResponses = [
        "NO DATA", "UNABLE TO CONNECT", "?", "ERROR",
        "BUS INIT", "CAN ERROR", "STOPPED"
    ]

    // MARK: - Init

    init(bluetoothManager: BluetoothManager, logger: OBDLogger? = nil) {
        self.bluetoothManager = bluetoothManager
        self.logger = logger
        subscribeToResponses()
    }

    // MARK: - Initialization Sequence

    /// Run the full ELM327 AT init sequence
    func initialize() async throws {
        isInitialized = false

        for command in OBDCommand.initSequence {
            let response = try await sendCommand(command)

            // ATZ returns adapter name and version, others return "OK"
            if command == OBDCommand.reset {
                guard response.contains("ELM") || response.contains("OK") || response.contains("AT") else {
                    logger?.logError("Init failed: unexpected reset response: \(response)")
                    throw OBDError.initializationFailed
                }
                // Wait for adapter to boot up
                try await Task.sleep(nanoseconds: 1_000_000_000)
            }
        }

        isInitialized = true
        logger?.logParsed("ELM327 initialized successfully")
    }

    // MARK: - Command Execution

    /// Send a command and wait for the complete response
    func sendCommand(_ command: String) async throws -> String {
        logger?.logSent(command)

        // Increment token so stale timeouts are ignored
        commandToken &+= 1
        let myToken = commandToken

        return try await withCheckedThrowingContinuation { continuation in
            // Cancel any existing pending response
            responseContinuation?.resume(throwing: OBDError.timeout)
            responseContinuation = continuation

            bluetoothManager.send(command)

            // Set up timeout — only fires if this command is still active
            DispatchQueue.main.asyncAfter(deadline: .now() + responseTimeout) { [weak self] in
                guard let self, self.commandToken == myToken else { return }
                self.responseContinuation?.resume(throwing: OBDError.timeout)
                self.responseContinuation = nil
            }
        }
    }

    /// Send an OBD/UDS command and parse the hex response bytes
    func sendAndParseHex(_ command: String) async throws -> [UInt8] {
        let response = try await sendCommand(command)

        // Check for error responses
        for errorPattern in Self.errorResponses {
            if response.contains(errorPattern) {
                if errorPattern == "NO DATA" || errorPattern == "STOPPED" {
                    throw OBDError.noResponse
                } else if errorPattern == "UNABLE TO CONNECT" {
                    throw OBDError.unsupportedProtocol
                } else {
                    throw OBDError.invalidResponse
                }
            }
        }

        let bytes = parseHexResponse(response)
        logger?.logParsed("Hex: \(bytes.map { String(format: "%02X", $0) }.joined(separator: " "))")

        // Check for UDS/KWP negative response (7F <service> <NRC>)
        if bytes.count >= 3 && bytes[0] == 0x7F {
            let nrc = bytes[2]
            let nrcDesc: String
            switch nrc {
            case 0x11: nrcDesc = "serviceNotSupported"
            case 0x12: nrcDesc = "subFunctionNotSupported"
            case 0x13: nrcDesc = "incorrectMessageLength"
            case 0x22: nrcDesc = "conditionsNotCorrect"
            case 0x31: nrcDesc = "requestOutOfRange"
            default: nrcDesc = String(format: "0x%02X", nrc)
            }
            logger?.logError("Negative response: \(nrcDesc)")
            throw OBDError.invalidResponse
        }

        return bytes
    }

    // MARK: - Response Parsing

    /// Parse a hex response string into bytes, handling multi-frame ISO-TP responses.
    /// Handles line prefixes like "0:", "1:" from multi-frame CAN, and strips
    /// echo/AT/status lines.
    func parseHexResponse(_ response: String) -> [UInt8] {
        let lines = response.components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines).uppercased() }
            .filter { !$0.isEmpty && $0 != ">" }

        var allBytes: [UInt8] = []

        for line in lines {
            // Skip echo/AT/status lines
            if line.hasPrefix("AT") || line.hasPrefix("ELM") || line.hasPrefix("OK") ||
               line.hasPrefix("SEARCHING") || line.hasPrefix("STOPPED") {
                continue
            }

            // Remove ISO-TP line prefix (e.g., "0:", "1:")
            var dataPart = line
            if let colonIndex = line.firstIndex(of: ":") {
                dataPart = String(line[line.index(after: colonIndex)...])
            } else if line.count <= 3 {
                // Likely a length header (e.g. "03E") — skip
                continue
            }

            // Remove spaces and non-hex, then parse hex pairs
            let hex = dataPart.components(separatedBy: CharacterSet.alphanumerics.inverted).joined()
            let bytes = hexStringToBytes(hex)
            allBytes.append(contentsOf: bytes)
        }

        return allBytes
    }

    /// Convert a hex string to byte array
    private func hexStringToBytes(_ hex: String) -> [UInt8] {
        var bytes: [UInt8] = []
        var index = hex.startIndex
        while index < hex.endIndex {
            let nextIndex = hex.index(index, offsetBy: 2, limitedBy: hex.endIndex) ?? hex.endIndex
            if nextIndex == hex.index(after: index) { break } // odd character left
            let byteString = String(hex[index..<nextIndex])
            if let byte = UInt8(byteString, radix: 16) {
                bytes.append(byte)
            } else {
                break // non-hex character encountered
            }
            index = nextIndex
        }
        return bytes
    }

    // MARK: - Private

    private func subscribeToResponses() {
        bluetoothManager.receivedDataPublisher
            .sink { [weak self] response in
                self?.logger?.logReceived(response)
                // Invalidate the timeout by advancing the token
                self?.commandToken &+= 1
                self?.responseContinuation?.resume(returning: response)
                self?.responseContinuation = nil
            }
            .store(in: &cancellables)
    }
}
