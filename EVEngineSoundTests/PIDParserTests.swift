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

    // MARK: - Pedal Position Parsing

    func testPedalPosition_fullThrottle() {
        // 0xFF = 255 => 1.0
        XCTAssertEqual(PIDParser.parsePedalPosition(from: [0xFF]), 1.0)
    }

    func testPedalPosition_noThrottle() {
        XCTAssertEqual(PIDParser.parsePedalPosition(from: [0x00]), 0.0)
    }

    func testPedalPosition_midRange() {
        // 0x80 = 128 => 128/255 ~= 0.502
        let result = PIDParser.parsePedalPosition(from: [0x80])
        XCTAssertEqual(result, 128.0 / 255.0, accuracy: 0.001)
    }

    func testPedalPosition_emptyData() {
        XCTAssertEqual(PIDParser.parsePedalPosition(from: []), 0.0)
    }

    func testPedalPosition_twoBytesUsesFirstByte() {
        // With two bytes, should use first byte normalized
        let result = PIDParser.parsePedalPosition(from: [0x80, 0x00])
        XCTAssertEqual(result, 128.0 / 255.0, accuracy: 0.001)
    }

    func testStandardPedalPosition() {
        XCTAssertEqual(PIDParser.parseStandardPedalPosition(from: [0xFF]), 1.0)
        XCTAssertEqual(PIDParser.parseStandardPedalPosition(from: [0x00]), 0.0)
        XCTAssertEqual(PIDParser.parseStandardPedalPosition(from: []), 0.0)
    }
}
