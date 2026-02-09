import Foundation

/// UDS (Unified Diagnostic Services) framing and ISO-TP support
enum UDSProtocol {

    // MARK: - Frame Types (ISO 15765-2 / ISO-TP)

    enum FrameType: UInt8 {
        case singleFrame = 0x00
        case firstFrame = 0x10
        case consecutiveFrame = 0x20
        case flowControl = 0x30
    }

    // MARK: - Request Building

    /// Build a UDS Read Data By Identifier (0x22) request
    static func buildReadRequest(did: UInt16) -> [UInt8] {
        return [
            OBDCommand.udsReadDataByID,
            UInt8((did >> 8) & 0xFF),
            UInt8(did & 0xFF)
        ]
    }

    // MARK: - Response Parsing

    /// Result of parsing a UDS response
    struct ParsedResponse {
        let serviceID: UInt8
        let did: UInt16
        let data: [UInt8]
    }

    /// Parse a positive UDS response for service 0x22 (returns 0x62)
    static func parsePositiveResponse(_ bytes: [UInt8]) throws -> ParsedResponse {
        // Strip CAN header if present (typically 3 bytes: priority, target, source)
        let payload = stripHeader(bytes)

        guard payload.count >= 4 else {
            throw OBDError.invalidResponse
        }

        // Check for negative response (0x7F)
        if payload[0] == 0x7F {
            throw negativeResponseError(payload)
        }

        // Positive response should start with 0x62 for service 0x22
        guard payload[0] == OBDCommand.udsPositiveResponse else {
            throw OBDError.invalidResponse
        }

        let did = (UInt16(payload[1]) << 8) | UInt16(payload[2])
        let data = Array(payload[3...])

        return ParsedResponse(serviceID: payload[0], did: did, data: data)
    }

    // MARK: - Multi-Frame (ISO-TP) Support

    /// Reassemble a multi-frame ISO-TP response
    static func reassembleMultiFrame(_ frames: [[UInt8]]) throws -> [UInt8] {
        guard let firstFrame = frames.first, firstFrame.count >= 2 else {
            throw OBDError.invalidResponse
        }

        let frameTypeByte = firstFrame[0] & 0xF0

        // Single frame
        if frameTypeByte == FrameType.singleFrame.rawValue {
            let length = Int(firstFrame[0] & 0x0F)
            guard firstFrame.count >= length + 1 else {
                throw OBDError.invalidResponse
            }
            return Array(firstFrame[1...length])
        }

        // First frame (0x10)
        guard frameTypeByte == FrameType.firstFrame.rawValue else {
            throw OBDError.invalidResponse
        }

        let totalLength = Int(UInt16(firstFrame[0] & 0x0F) << 8 | UInt16(firstFrame[1]))
        var data = Array(firstFrame[2...])

        // Consecutive frames (0x2X)
        for i in 1..<frames.count {
            let frame = frames[i]
            guard !frame.isEmpty else { continue }
            let typeByte = frame[0] & 0xF0
            if typeByte == FrameType.consecutiveFrame.rawValue {
                data.append(contentsOf: frame[1...])
            }
        }

        // Trim to declared total length
        if data.count > totalLength {
            data = Array(data.prefix(totalLength))
        }

        return data
    }

    /// Build a flow control frame to send back to the ECU
    static func buildFlowControl(blockSize: UInt8 = 0, separationTime: UInt8 = 0) -> [UInt8] {
        return [FrameType.flowControl.rawValue, blockSize, separationTime, 0x00, 0x00, 0x00, 0x00, 0x00]
    }

    // MARK: - Helpers

    /// Strip CAN header bytes if present
    private static func stripHeader(_ bytes: [UInt8]) -> [UInt8] {
        // Common CAN response header: 7E8 (3 bytes as hex), or single PCI byte
        // If first byte looks like a UDS response (0x62, 0x7F) or ISO-TP PCI, use as-is
        guard bytes.count >= 4 else { return bytes }

        // If we have a typical 3-byte CAN header (e.g., 7E8 decoded)
        // Check if byte at index 0 is a high value typical of CAN IDs
        if bytes[0] > 0x70 && bytes[0] != 0x7F {
            // Likely has header bytes; skip until we find the PCI byte or service response
            // Look for the ISO-TP PCI byte or direct service response
            for i in 0..<min(4, bytes.count) {
                let candidate = bytes[i]
                if candidate == OBDCommand.udsPositiveResponse || candidate == 0x7F ||
                   (candidate & 0xF0) == 0x00 || (candidate & 0xF0) == 0x10 {
                    return Array(bytes[i...])
                }
            }
        }

        return bytes
    }

    /// Map a UDS negative response to an error
    private static func negativeResponseError(_ payload: [UInt8]) -> OBDError {
        // 0x7F [service_id] [NRC]
        // Common NRCs: 0x11 serviceNotSupported, 0x12 subFunctionNotSupported,
        // 0x22 conditionsNotCorrect, 0x31 requestOutOfRange
        return .invalidResponse
    }
}
