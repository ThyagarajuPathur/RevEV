import SwiftUI
import Combine

// MARK: - ViewModel

final class AccPedalDebugViewModel: ObservableObject {

    // MARK: - Published State

    @Published var rawResponse: String = "—"
    @Published var rawBytes: [UInt8] = []
    /// Data bytes only (after stripping 62 01 01 header)
    @Published var dataBytes: [UInt8] = []
    @Published var status: String = "Idle"
    @Published var isPolling = false
    @Published var log: [String] = []

    /// User-editable command & header
    @Published var command: String = "220101"
    @Published var header: String = "7E4"

    // MARK: - Private

    private let bluetoothManager: BluetoothManager
    private let obdService: OBDService
    private var cancellable: AnyCancellable?
    private var pollTask: Task<Void, Never>?
    private var currentHeader: String = ""

    init(bluetoothManager: BluetoothManager, obdService: OBDService) {
        self.bluetoothManager = bluetoothManager
        self.obdService = obdService
    }

    // MARK: - Setup

    func setup() {
        obdService.stop()
        status = "OBD polling stopped. Setting header…"
        appendLog("Stopped OBD polling")

        cancellable = bluetoothManager.receivedDataPublisher
            .receive(on: DispatchQueue.main)
            .sink { [weak self] response in
                self?.handleResponse(response)
            }

        setHeader(header)
    }

    func setHeader(_ h: String) {
        let cmd = "ATSH\(h)"
        currentHeader = h
        appendLog("TX → \(cmd)")
        sendRaw(cmd)
    }

    // MARK: - Single Request

    func sendOnce() {
        let cmd = command.trimmingCharacters(in: .whitespaces)
        status = "Sending \(cmd) → \(currentHeader)…"
        appendLog("TX → \(cmd)")
        sendRaw(cmd)
    }

    func sendCustom(_ cmd: String) {
        let trimmed = cmd.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { return }
        appendLog("TX → \(trimmed)")
        sendRaw(trimmed)
    }

    // MARK: - Continuous Polling

    func startPolling() {
        guard !isPolling else { return }
        isPolling = true
        appendLog("Polling started (500 ms interval)")

        pollTask = Task { [weak self] in
            while !Task.isCancelled {
                await MainActor.run { self?.sendOnce() }
                try? await Task.sleep(nanoseconds: 500_000_000)
            }
        }
    }

    func stopPolling() {
        pollTask?.cancel()
        pollTask = nil
        isPolling = false
        status = "Polling stopped"
        appendLog("Polling stopped")
    }

    // MARK: - Teardown

    func teardown() {
        stopPolling()
        cancellable?.cancel()
        cancellable = nil
        sendRaw("ATSH7E4")
        appendLog("Restored header to 7E4 (BMS)")

        // Restart OBD polling that was stopped in setup().
        // Small delay lets the ATSH7E4 command complete before polls resume.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self] in
            self?.obdService.startPolling()
            self?.appendLog("OBD polling restarted")
        }
        status = "Cleaned up — restarting OBD…"
    }

    // MARK: - Private Helpers

    private func sendRaw(_ command: String) {
        bluetoothManager.send(command)
    }

    private func handleResponse(_ response: String) {
        rawResponse = response.trimmingCharacters(in: .whitespacesAndNewlines)
        appendLog("RX ← \(rawResponse)")

        if rawResponse.contains("OK") {
            status = "Header set to \(currentHeader) — Ready"
            appendLog("Header confirmed \(currentHeader)")
            return
        }

        let bytes = parseHex(rawResponse)
        rawBytes = bytes

        // Check for negative response
        if bytes.count >= 3 && bytes[0] == 0x7F {
            dataBytes = []
            let svc = String(format: "0x%02X", bytes[1])
            let nrc = bytes[2]
            let nrcDesc: String
            switch nrc {
            case 0x11: nrcDesc = "serviceNotSupported"
            case 0x12: nrcDesc = "subFunctionNotSupported"
            case 0x13: nrcDesc = "incorrectMessageLength"
            case 0x22: nrcDesc = "conditionsNotCorrect"
            case 0x31: nrcDesc = "requestOutOfRange"
            case 0x33: nrcDesc = "securityAccessDenied"
            case 0x78: nrcDesc = "responsePending"
            default:   nrcDesc = String(format: "0x%02X", nrc)
            }
            status = "NEGATIVE: svc \(svc) NRC \(nrcDesc)"
            appendLog("⚠ Negative response: service \(svc), NRC \(nrcDesc)")
            return
        }

        // Extract data bytes after response header
        let dataStart = findDataStart(bytes)
        if dataStart >= 0 && dataStart < bytes.count {
            dataBytes = Array(bytes[dataStart...])
            status = "\(dataBytes.count) data bytes (from offset \(dataStart))"
        } else {
            dataBytes = bytes // show everything if no header found
            status = "\(bytes.count) raw bytes (no header found)"
        }
    }

    private func findDataStart(_ bytes: [UInt8]) -> Int {
        if let idx = bytes.firstIndex(of: 0x62), idx + 2 < bytes.count {
            return idx + 3 // 62 XX XX <data>
        }
        if let idx = bytes.firstIndex(of: 0x61), idx + 1 < bytes.count {
            return idx + 2 // 61 XX <data>
        }
        return -1
    }

    private func parseHex(_ response: String) -> [UInt8] {
        let lines = response.components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines).uppercased() }
            .filter { !$0.isEmpty && $0 != ">" }

        var allBytes: [UInt8] = []

        for line in lines {
            if line.hasPrefix("AT") || line.hasPrefix("ELM") || line.hasPrefix("OK") ||
               line.hasPrefix("SEARCHING") || line.hasPrefix("STOPPED") {
                continue
            }

            var dataPart = line
            if let colon = line.firstIndex(of: ":") {
                dataPart = String(line[line.index(after: colon)...])
            } else if line.count <= 3 {
                continue
            }

            let hex = dataPart.components(separatedBy: CharacterSet.alphanumerics.inverted).joined()
            var idx = hex.startIndex
            while idx < hex.endIndex {
                guard let next = hex.index(idx, offsetBy: 2, limitedBy: hex.endIndex),
                      next != hex.index(after: idx) else { break }
                if let byte = UInt8(String(hex[idx..<next]), radix: 16) {
                    allBytes.append(byte)
                } else { break }
                idx = next
            }
        }
        return allBytes
    }

    private func appendLog(_ msg: String) {
        let ts = Self.formatter.string(from: Date())
        log.append("[\(ts)] \(msg)")
        if log.count > 200 { log.removeFirst(log.count - 200) }
    }

    private static let formatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss.SSS"
        return f
    }()
}

// MARK: - View

struct AccPedalDebugView: View {
    @EnvironmentObject private var coordinator: AppCoordinator
    @StateObject private var vm: AccPedalDebugViewModel
    @State private var customCmd: String = ""

    init(bluetoothManager: BluetoothManager, obdService: OBDService) {
        _vm = StateObject(wrappedValue: AccPedalDebugViewModel(
            bluetoothManager: bluetoothManager,
            obdService: obdService
        ))
    }

    var body: some View {
        NavigationView {
            VStack(spacing: 0) {
                statusBanner
                commandInputSection.padding(.horizontal, 12).padding(.top, 8)
                actionButtons.padding(.horizontal, 12).padding(.vertical, 8)
                byteTableSection
            }
            .navigationTitle("Byte Inspector")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Clear") { vm.log.removeAll() }
                }
            }
            .onAppear { vm.setup() }
            .onDisappear { vm.teardown() }
        }
    }

    // MARK: - Sub-views

    private var statusBanner: some View {
        HStack {
            Circle()
                .fill(vm.isPolling ? Color.green : Color.orange)
                .frame(width: 10, height: 10)
            Text(vm.status)
                .font(.caption)
                .fontWeight(.medium)
                .lineLimit(2)
            Spacer()
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .background((vm.isPolling ? Color.green : Color.orange).opacity(0.15))
    }

    private var commandInputSection: some View {
        VStack(spacing: 8) {
            HStack(spacing: 8) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Header").font(.system(size: 10)).foregroundColor(.secondary)
                    TextField("7E4", text: $vm.header)
                        .textFieldStyle(.roundedBorder)
                        .font(.system(size: 14, design: .monospaced))
                        .autocapitalization(.allCharacters)
                        .disableAutocorrection(true)
                        .frame(width: 70)
                        .onSubmit { vm.setHeader(vm.header) }
                }

                VStack(alignment: .leading, spacing: 2) {
                    Text("Command").font(.system(size: 10)).foregroundColor(.secondary)
                    TextField("220101", text: $vm.command)
                        .textFieldStyle(.roundedBorder)
                        .font(.system(size: 14, design: .monospaced))
                        .autocapitalization(.allCharacters)
                        .disableAutocorrection(true)
                        .onSubmit { vm.sendOnce() }
                }

                Button {
                    vm.setHeader(vm.header)
                } label: {
                    Image(systemName: "arrow.triangle.2.circlepath")
                }
                .buttonStyle(.bordered)
                .padding(.top, 14)
            }

            HStack {
                TextField("Custom AT/OBD cmd…", text: $customCmd)
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: 13, design: .monospaced))
                    .autocapitalization(.allCharacters)
                    .disableAutocorrection(true)
                    .onSubmit { sendCustom() }
                Button("Send") { sendCustom() }
                    .buttonStyle(.borderedProminent)
                    .tint(.purple)
            }
        }
    }

    private var actionButtons: some View {
        HStack(spacing: 12) {
            Button {
                vm.sendOnce()
            } label: {
                Label("Send", systemImage: "arrow.up.circle")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .disabled(vm.isPolling)

            Button {
                vm.startPolling()
            } label: {
                Label("Poll", systemImage: "play.fill")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(.green)
            .disabled(vm.isPolling)

            Button {
                vm.stopPolling()
            } label: {
                Label("Stop", systemImage: "stop.fill")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(.red)
            .disabled(!vm.isPolling)
        }
    }

    /// Full byte table: offset | hex | decimal | bar
    private var byteTableSection: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Table header
            HStack(spacing: 0) {
                Text("Off")
                    .frame(width: 32, alignment: .leading)
                Text("Hex")
                    .frame(width: 32, alignment: .leading)
                Text("Dec")
                    .frame(width: 36, alignment: .trailing)
                Text("Val")
                    .frame(width: 40, alignment: .trailing)
                Text("")
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .font(.system(size: 10, weight: .bold, design: .monospaced))
            .foregroundColor(.gray)
            .padding(.horizontal, 8)
            .padding(.vertical, 4)
            .background(Color.black)

            Divider().background(Color.gray)

            // Byte rows
            ScrollView {
                LazyVStack(spacing: 0) {
                    ForEach(Array(vm.dataBytes.enumerated()), id: \.offset) { idx, byte in
                        byteRow(offset: idx, byte: byte)
                    }
                }
            }
            .background(Color.black)
        }
    }

    private func byteRow(offset: Int, byte: UInt8) -> some View {
        let fraction = CGFloat(byte) / 255.0
        let letter = offset < 26 ? String(UnicodeScalar(UInt8(97) + UInt8(offset))) : "\(offset)"

        return HStack(spacing: 0) {
            // Offset + letter label
            Text("\(String(format: "%02d", offset)) \(letter)")
                .frame(width: 40, alignment: .leading)
                .foregroundColor(.gray)

            // Hex value
            Text(String(format: "%02X", byte))
                .frame(width: 28, alignment: .leading)
                .foregroundColor(.cyan)

            // Decimal value
            Text("\(byte)")
                .frame(width: 36, alignment: .trailing)
                .foregroundColor(.white)

            // Value as percentage of 255
            Text(String(format: "%5.1f%%", fraction * 100))
                .frame(width: 52, alignment: .trailing)
                .foregroundColor(.orange)

            // Visual bar
            GeometryReader { geo in
                Rectangle()
                    .fill(barColor(fraction))
                    .frame(width: geo.size.width * fraction, height: 12)
                    .cornerRadius(2)
            }
            .frame(height: 12)
            .padding(.leading, 6)
        }
        .font(.system(size: 11, design: .monospaced))
        .padding(.horizontal, 8)
        .padding(.vertical, 2)
        .background(offset % 2 == 0 ? Color.black : Color.white.opacity(0.05))
    }

    private func barColor(_ fraction: CGFloat) -> Color {
        if fraction < 0.25 { return .green }
        if fraction < 0.50 { return .yellow }
        if fraction < 0.75 { return .orange }
        return .red
    }

    private func sendCustom() {
        guard !customCmd.isEmpty else { return }
        vm.sendCustom(customCmd)
        customCmd = ""
    }
}
