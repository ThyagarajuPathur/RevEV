import XCTest
@testable import EVEngineSound

final class PIDParserTests: XCTestCase {

    // MARK: - Standard RPM Parsing

    func testStandardRPM_knownValues() {
        // Formula: ((A*256)+B)/4
        // [0x0C, 0x00] => (12*256 + 0)/4 = 768 RPM
        XCTAssertEqual(PIDParser.parseStandardRPM(from: [0x0C, 0x00]), 768.0)

        // [0x00, 0x00] => 0 RPM
        XCTAssertEqual(PIDParser.parseStandardRPM(from: [0x00, 0x00]), 0.0)

        // [0x1A, 0xF8] => (26*256 + 248)/4 = (6904)/4 = 1726 RPM
        XCTAssertEqual(PIDParser.parseStandardRPM(from: [0x1A, 0xF8]), 1726.0)

        // [0xFF, 0xFF] => (255*256 + 255)/4 = 16383.75 RPM (max)
        XCTAssertEqual(PIDParser.parseStandardRPM(from: [0xFF, 0xFF]), 16383.75)
    }

    func testStandardRPM_idleRange() {
        // Typical idle ~800 RPM: ((A*256)+B)/4 = 800 => A*256+B = 3200
        // 3200 = 0x0C80 => A=0x0C, B=0x80
        XCTAssertEqual(PIDParser.parseStandardRPM(from: [0x0C, 0x80]), 800.0)
    }

    func testStandardRPM_emptyData() {
        XCTAssertEqual(PIDParser.parseStandardRPM(from: []), 0.0)
    }

    func testStandardRPM_singleByte() {
        XCTAssertEqual(PIDParser.parseStandardRPM(from: [0x0C]), 0.0)
    }

    // MARK: - Hyundai UDS RPM Parsing

    func testHyundaiRPM_positiveValues() {
        // Signed 16-bit MSB first
        // [0x03, 0x20] => 800 RPM
        XCTAssertEqual(PIDParser.parseHyundaiRPM(from: [0x03, 0x20]), 800.0)

        // [0x00, 0x00] => 0 RPM
        XCTAssertEqual(PIDParser.parseHyundaiRPM(from: [0x00, 0x00]), 0.0)

        // [0x1F, 0x40] => 8000 RPM
        XCTAssertEqual(PIDParser.parseHyundaiRPM(from: [0x1F, 0x40]), 8000.0)
    }

    func testHyundaiRPM_negativeValueClampsToZero() {
        // Negative values (e.g., [0xFF, 0xFE] = -2) should clamp to 0
        XCTAssertEqual(PIDParser.parseHyundaiRPM(from: [0xFF, 0xFE]), 0.0)
    }

    func testHyundaiRPM_emptyData() {
        XCTAssertEqual(PIDParser.parseHyundaiRPM(from: []), 0.0)
    }

    func testHyundaiRPM_singleByte() {
        XCTAssertEqual(PIDParser.parseHyundaiRPM(from: [0x03]), 0.0)
    }

    // MARK: - parseRPM auto-detection

    func testParseRPM_standardMode() {
        XCTAssertEqual(PIDParser.parseRPM(from: [0x0C, 0x00], isUDS: false), 768.0)
    }

    func testParseRPM_udsMode() {
        XCTAssertEqual(PIDParser.parseRPM(from: [0x03, 0x20], isUDS: true), 800.0)
    }

    func testParseRPM_emptyData() {
        XCTAssertEqual(PIDParser.parseRPM(from: []), 0.0)
    }

    // MARK: - Hyundai Pedal Position Parsing (bytes 10-11 wrapping counter)

    func testHyundaiPedal_fullThrottle() {
        // Byte 10=3, Byte 11=255 => raw = 3*256 + 255 = 1023 => 1023/1023 = 1.0
        // Build a response with 62 01 01 header + 12 data bytes (need index 10,11)
        var bytes: [UInt8] = [0x62, 0x01, 0x01]  // header
        bytes += Array(repeating: 0x00, count: 10) // data bytes 0-9
        bytes += [0x03, 0xFF]                       // data bytes 10=3, 11=255
        XCTAssertEqual(PIDParser.parseHyundaiPedal(from: bytes), 1.0, accuracy: 0.001)
    }

    func testHyundaiPedal_noThrottle() {
        // Byte 10=0, Byte 11=0 => raw = 0 => 0.0
        var bytes: [UInt8] = [0x62, 0x01, 0x01]
        bytes += Array(repeating: 0x00, count: 12) // 12 zeros (bytes 0-11)
        XCTAssertEqual(PIDParser.parseHyundaiPedal(from: bytes), 0.0)
    }

    func testHyundaiPedal_slightPress() {
        // Byte 10=0, Byte 11=120 => raw = 120 => 120/1023 ≈ 0.117
        var bytes: [UInt8] = [0x62, 0x01, 0x01]
        bytes += Array(repeating: 0x00, count: 10)
        bytes += [0x00, 120]  // byte10=0, byte11=120
        XCTAssertEqual(PIDParser.parseHyundaiPedal(from: bytes), 120.0 / 1023.0, accuracy: 0.001)
    }

    func testHyundaiPedal_midRange() {
        // Byte 10=2, Byte 11=0 => raw = 512 => 512/1023 ≈ 0.501
        var bytes: [UInt8] = [0x62, 0x01, 0x01]
        bytes += Array(repeating: 0x00, count: 10)
        bytes += [0x02, 0x00]
        XCTAssertEqual(PIDParser.parseHyundaiPedal(from: bytes), 512.0 / 1023.0, accuracy: 0.001)
    }

    func testHyundaiPedal_wrapAround() {
        // Byte 10=1, Byte 11=0 => raw = 256 (byte 11 just wrapped from 255→0)
        var bytes: [UInt8] = [0x62, 0x01, 0x01]
        bytes += Array(repeating: 0x00, count: 10)
        bytes += [0x01, 0x00]
        XCTAssertEqual(PIDParser.parseHyundaiPedal(from: bytes), 256.0 / 1023.0, accuracy: 0.001)
    }

    func testHyundaiPedal_emptyData() {
        XCTAssertEqual(PIDParser.parseHyundaiPedal(from: []), 0.0)
    }

    func testHyundaiPedal_tooShort() {
        // Only header, no data — should return 0
        XCTAssertEqual(PIDParser.parseHyundaiPedal(from: [0x62, 0x01, 0x01]), 0.0)
    }

    // MARK: - Standard Pedal Position

    func testStandardPedalPosition() {
        XCTAssertEqual(PIDParser.parseStandardPedalPosition(from: [0xFF]), 1.0)
        XCTAssertEqual(PIDParser.parseStandardPedalPosition(from: [0x00]), 0.0)
        XCTAssertEqual(PIDParser.parseStandardPedalPosition(from: []), 0.0)
    }
}
