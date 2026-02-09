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

    /// Max raw pedal value for normalization.
    /// Hyundai/Kia EVs use a 16-bit wrapping counter across bytes 10-11.
    /// 1023 (10-bit) is the typical pedal sensor range; tune if testing shows otherwise.
    private static let pedalRawMax: Double = 1023.0

    /// Parse accelerator pedal from Hyundai/Kia BMS DID 0x0101 response (ECU 7E4).
    ///
    /// Response: [62, 01, 01, <data...>]
    /// Pedal position is encoded as a 16-bit wrapping counter at data bytes 10-11:
    ///   - Byte 10 (high): increments by 1 each time byte 11 wraps past 255
    ///   - Byte 11 (low):  counts 0-255 as pedal is pressed further
    ///   - Raw value = byte10 * 256 + byte11
    ///   - Normalized to 0.0-1.0 by dividing by `pedalRawMax` (1023)
    ///
    /// Example: slight press -> byte10=0, byte11=120 -> raw=120 -> ~11.7%
    ///          full press   -> byte10=3, byte11=255 -> raw=1023 -> 100%
    static func parseHyundaiPedal(from bytes: [UInt8]) -> Double {
        guard let dataStart = findHeader([0x62, 0x01, 0x01], in: bytes) else { return 0 }
        guard dataStart + 11 < bytes.count else { return 0 }
        let high = UInt16(bytes[dataStart + 10])
        let low  = UInt16(bytes[dataStart + 11])
        let raw = Double(high * 256 + low)
        return min(1.0, max(0.0, raw / pedalRawMax))
    }

    /// Parse standard OBD-II pedal position (PID 0x49).
    /// Formula: A * 100 / 255 for percentage, then normalize to 0.0-1.0
    static func parseStandardPedalPosition(from bytes: [UInt8]) -> Double {
        guard !bytes.isEmpty else { return 0 }
        return min(1.0, max(0.0, Double(bytes[0]) / 255.0))
    }
}
