import Foundation

/// Parses raw OBD/UDS response bytes into usable values
enum PIDParser {

    // MARK: - RPM Parsing

    /// Parse RPM from standard OBD-II PID 0x0C response bytes.
    /// Formula: ((A * 256) + B) / 4
    static func parseStandardRPM(from bytes: [UInt8]) -> Double {
        guard bytes.count >= 2 else { return 0 }
        let raw = (Double(bytes[0]) * 256.0 + Double(bytes[1])) / 4.0
        return max(0, raw)
    }

    /// RPM data offset within the DID 0x0101 response data (after 62 01 01 header)
    /// Offset 53-54: Signed 16-bit motor RPM (-10100 to 10100)
    private static let rpmDataOffset = 53

    /// Parse RPM from Hyundai/Kia BMS DID 0x0101 response (ECU 7E4).
    /// Response: [62, 01, 01, <data...>]
    /// RPM at data offset 53-54 (signed Int16 big-endian)
    static func parseHyundaiRPM(from bytes: [UInt8]) -> Double {
        // Find 62 01 01 header and get index of first data byte after it
        guard let dataStart = findHeader([0x62, 0x01, 0x01], in: bytes) else { return 0 }
        guard dataStart + rpmDataOffset + 1 < bytes.count else { return 0 }
        let a = bytes[dataStart + rpmDataOffset]       // high byte
        let b = bytes[dataStart + rpmDataOffset + 1]   // low byte
        let rpm = Int(Int16(bitPattern: UInt16(a) << 8 | UInt16(b)))
        return Double(abs(rpm))
    }

    /// Find a header byte sequence in the response.
    /// Returns the index of the first byte AFTER the header (i.e., first data byte).
    private static func findHeader(_ header: [UInt8], in bytes: [UInt8]) -> Int? {
        guard bytes.count >= header.count else { return nil }
        for i in 0...(bytes.count - header.count) {
            if Array(bytes[i..<(i + header.count)]) == header {
                return i + header.count
            }
        }
        return nil
    }

    // MARK: - Pedal Position Parsing

    /// Parse accelerator pedal from Hyundai/Kia BMS DID 0x0101 response (ECU 7E4).
    /// Response: [62, 01, 01, <data...>]
    /// Pedal at data offset 14: M / 2 = percent, normalized to 0.0-1.0
    /// Returns normalized 0.0 to 1.0
    static func parseHyundaiPedal(from bytes: [UInt8]) -> Double {
        guard let dataStart = findHeader([0x62, 0x01, 0x01], in: bytes) else { return 0 }
        guard dataStart + 14 < bytes.count else { return 0 }
        let m = Double(bytes[dataStart + 14])
        let percent = m / 2.0 // 0-100%
        return min(1.0, max(0.0, percent / 100.0))
    }

    /// Parse standard OBD-II pedal position (PID 0x49).
    /// Formula: A * 100 / 255 for percentage, then normalize to 0.0-1.0
    static func parseStandardPedalPosition(from bytes: [UInt8]) -> Double {
        guard !bytes.isEmpty else { return 0 }
        return min(1.0, max(0.0, Double(bytes[0]) / 255.0))
    }
}
