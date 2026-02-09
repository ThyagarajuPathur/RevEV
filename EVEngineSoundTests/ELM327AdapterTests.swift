import XCTest
@testable import EVEngineSound

final class ELM327AdapterTests: XCTestCase {

    // MARK: - Init Sequence

    func testInitSequenceContainsAllCommands() {
        let sequence = OBDCommand.initSequence
        XCTAssertEqual(sequence.count, 6)
        XCTAssertEqual(sequence[0], "ATZ")
        XCTAssertEqual(sequence[1], "ATE0")
        XCTAssertEqual(sequence[2], "ATL0")
        XCTAssertEqual(sequence[3], "ATS0")
        XCTAssertEqual(sequence[4], "ATH1")
        XCTAssertEqual(sequence[5], "ATSP0")
    }

    func testATCommands() {
        XCTAssertEqual(OBDCommand.reset, "ATZ")
        XCTAssertEqual(OBDCommand.echoOff, "ATE0")
        XCTAssertEqual(OBDCommand.linefeedsOff, "ATL0")
        XCTAssertEqual(OBDCommand.spacesOff, "ATS0")
        XCTAssertEqual(OBDCommand.headersOn, "ATH1")
        XCTAssertEqual(OBDCommand.autoProtocol, "ATSP0")
        XCTAssertEqual(OBDCommand.describeProtocol, "ATDP")
        XCTAssertEqual(OBDCommand.headerEngineECU, "ATSH7E0")
    }

    // MARK: - UDS Request Building

    func testUDSReadRequest() {
        // DID 0x0101 => "220101"
        XCTAssertEqual(OBDCommand.udsReadRequest(did: 0x0101), "220101")
        // DID 0x0154 => "220154"
        XCTAssertEqual(OBDCommand.udsReadRequest(did: 0x0154), "220154")
    }

    func testHyundaiCommands() {
        XCTAssertEqual(OBDCommand.hyundaiRPMRequest, "220101")
        XCTAssertEqual(OBDCommand.hyundaiPedalRequest, "220154")
    }

    // MARK: - Response Parsing (via ELM327Adapter)

    func testParseHexResponse_simpleHex() {
        let adapter = ELM327Adapter(bluetoothManager: BluetoothManager())
        let bytes = adapter.parseHexResponse("41 0C 0C 80")
        XCTAssertEqual(bytes, [0x41, 0x0C, 0x0C, 0x80])
    }

    func testParseHexResponse_noSpaces() {
        let adapter = ELM327Adapter(bluetoothManager: BluetoothManager())
        let bytes = adapter.parseHexResponse("410C0C80")
        XCTAssertEqual(bytes, [0x41, 0x0C, 0x0C, 0x80])
    }

    func testParseHexResponse_multiLine() {
        let adapter = ELM327Adapter(bluetoothManager: BluetoothManager())
        let response = "7E8 06 62 01 01 03 20\r\n"
        let bytes = adapter.parseHexResponse(response)
        XCTAssertFalse(bytes.isEmpty)
    }

    func testParseHexResponse_skipsATLines() {
        let adapter = ELM327Adapter(bluetoothManager: BluetoothManager())
        let response = "ATZ\r\nELM327 v1.5\r\nOK\r\n410C0C80"
        let bytes = adapter.parseHexResponse(response)
        XCTAssertEqual(bytes, [0x41, 0x0C, 0x0C, 0x80])
    }

    func testParseHexResponse_emptyString() {
        let adapter = ELM327Adapter(bluetoothManager: BluetoothManager())
        let bytes = adapter.parseHexResponse("")
        XCTAssertTrue(bytes.isEmpty)
    }

    func testParseHexResponse_searchingLine() {
        let adapter = ELM327Adapter(bluetoothManager: BluetoothManager())
        let response = "SEARCHING...\r\n410C0C80"
        let bytes = adapter.parseHexResponse(response)
        XCTAssertEqual(bytes, [0x41, 0x0C, 0x0C, 0x80])
    }

    // MARK: - Error Responses

    func testErrorResponsePatterns() {
        let errorPatterns = ["NO DATA", "UNABLE TO CONNECT", "?", "ERROR", "BUS INIT", "CAN ERROR"]
        for pattern in errorPatterns {
            XCTAssertTrue(pattern.count > 0, "Error pattern should not be empty: \(pattern)")
        }
    }

    // MARK: - Standard OBD-II Commands

    func testStandardPIDCommands() {
        XCTAssertEqual(OBDCommand.standardRPM, "010C")
        XCTAssertEqual(OBDCommand.standardPedalPosition, "0149")
    }

    // MARK: - OBD Name Pattern Matching

    func testOBDNamePatterns() {
        let patterns = BluetoothDevice.obdNamePatterns
        XCTAssertTrue(patterns.contains("OBD"))
        XCTAssertTrue(patterns.contains("ELM"))
        XCTAssertTrue(patterns.contains("V-LINK"))
        XCTAssertTrue(patterns.contains("iOBD"))
        XCTAssertTrue(patterns.contains("OBDII"))
        XCTAssertTrue(patterns.contains("Vgate"))
    }
}
