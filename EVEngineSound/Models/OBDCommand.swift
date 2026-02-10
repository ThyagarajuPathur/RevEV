import Foundation

/// ELM327 AT commands and OBD/UDS PIDs
enum OBDCommand {

    // MARK: - ELM327 AT Commands

    /// Reset adapter
    static let reset = "ATZ"
    /// Echo off
    static let echoOff = "ATE0"
    /// Linefeeds off
    static let linefeedsOff = "ATL0"
    /// Spaces off
    static let spacesOff = "ATS0"
    /// Headers off (clean responses for standard OBD-II)
    static let headersOff = "ATH0"
    /// Headers on (needed for UDS / multi-frame parsing)
    static let headersOn = "ATH1"
    /// Auto-detect protocol
    static let autoProtocol = "ATSP0"
    /// Force CAN 11-bit 500kbps (ISO 15765-4)
    static let canProtocol6 = "ATSP6"
    /// Describe current protocol
    static let describeProtocol = "ATDP"
    /// Set header to engine ECU (7E0)
    static let headerEngineECU = "ATSH7E0"

    /// Set header to BMS ECU (7E4) — used for Hyundai/Kia EV motor RPM
    static let headerBMS = "ATSH7E4"

    /// Initialization sequence for ELM327 (Hyundai/Kia EV mode)
    static let initSequence: [String] = [
        reset,
        echoOff,
        linefeedsOff,
        canProtocol6,
        headerBMS
    ]

    // MARK: - Standard OBD-II PIDs (Mode 01)

    /// Engine RPM - standard PID 0x0C (Mode 01)
    static let standardRPM = "010C"
    /// Accelerator pedal position - standard PID 0x49 (Mode 01)
    static let standardPedalPosition = "0149"

    // MARK: - Hyundai/Kia EV BMS PIDs (Service 0x22, ECU 7E4)

    /// BMS DID 0x0101 (service 0x22, ECU 7E4) — used for both RPM and battery data
    /// Response: 62 01 01 <data>; RPM at data offset 53-54 (signed Int16 big-endian)
    static let hyundaiRPMRequest = "220101"

    /// Accelerator Pedal DID 0x0154 (service 0x22, ECU 7E4)
    static let hyundaiPedalRequest = "220154"

    // MARK: - UDS Service 0x22 (Read Data By Identifier)

    /// UDS service ID for Read Data By Identifier
    static let udsReadDataByID: UInt8 = 0x22
    /// UDS positive response for service 0x22
    static let udsPositiveResponse: UInt8 = 0x62
    /// KWP service 0x21 positive response
    static let kwpPositiveResponse: UInt8 = 0x61

    /// Hyundai/Kia Motor RPM DID 0x0101
    static let hyundaiRPM_DID: UInt16 = 0x0101
    /// Hyundai/Kia Accelerator Pedal DID 0x0154
    static let hyundaiPedal_DID: UInt16 = 0x0154

    /// Build a UDS read request command string for the given DID
    static func udsReadRequest(did: UInt16) -> String {
        let serviceID = String(format: "%02X", udsReadDataByID)
        let didHigh = String(format: "%02X", (did >> 8) & 0xFF)
        let didLow = String(format: "%02X", did & 0xFF)
        return serviceID + didHigh + didLow
    }
}
