import SwiftUI

/// Bluetooth device scanning and connection view
struct ConnectionView: View {
    @ObservedObject var viewModel: ConnectionViewModel

    var body: some View {
        NavigationView {
            VStack(spacing: 0) {
                connectionStatusBanner
                deviceList
            }
            .navigationTitle("Connect OBD")
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    scanButton
                }
            }
            .alert("Connection Error",
                   isPresented: Binding(
                       get: { viewModel.errorMessage != nil },
                       set: { if !$0 { viewModel.errorMessage = nil } }
                   )) {
                Button("OK") { viewModel.errorMessage = nil }
            } message: {
                Text(viewModel.errorMessage ?? "")
            }
        }
    }

    // MARK: - Connection Status Banner

    private var connectionStatusBanner: some View {
        HStack {
            Circle()
                .fill(statusColor)
                .frame(width: 10, height: 10)
            Text(statusText)
                .font(.subheadline)
                .foregroundColor(.secondary)
            Spacer()
            if viewModel.isConnected {
                Button("Disconnect") {
                    viewModel.disconnect()
                }
                .font(.subheadline)
                .foregroundColor(.red)
            }
        }
        .padding(.horizontal)
        .padding(.vertical, 10)
        .background(Color(.systemGroupedBackground))
    }

    // MARK: - Device List

    private var deviceList: some View {
        Group {
            if viewModel.devices.isEmpty && viewModel.isScanning {
                scanningPlaceholder
            } else if viewModel.devices.isEmpty {
                emptyState
            } else {
                List(viewModel.devices) { device in
                    deviceRow(device)
                }
                .listStyle(.insetGrouped)
            }
        }
    }

    private func deviceRow(_ device: BluetoothDevice) -> some View {
        Button {
            viewModel.connect(to: device)
        } label: {
            HStack {
                VStack(alignment: .leading, spacing: 4) {
                    Text(device.name)
                        .font(.body)
                        .foregroundColor(.primary)
                    Text(signalDescription(rssi: device.rssi))
                        .font(.caption)
                        .foregroundColor(.secondary)
                }
                Spacer()
                signalBars(rssi: device.rssi)
                if viewModel.connectionState == .connecting {
                    ProgressView()
                        .padding(.leading, 8)
                }
            }
            .padding(.vertical, 4)
        }
        .disabled(viewModel.connectionState == .connecting)
    }

    // MARK: - Scanning Placeholder

    private var scanningPlaceholder: some View {
        VStack(spacing: 16) {
            Spacer()
            ProgressView()
                .scaleEffect(1.5)
            Text("Scanning for OBD adapters...")
                .font(.headline)
                .foregroundColor(.secondary)
            Text("Make sure your adapter is powered on and nearby")
                .font(.caption)
                .foregroundColor(.secondary)
                .multilineTextAlignment(.center)
            Spacer()
        }
        .padding()
    }

    // MARK: - Empty State

    private var emptyState: some View {
        VStack(spacing: 16) {
            Spacer()
            Image(systemName: "antenna.radiowaves.left.and.right")
                .font(.system(size: 48))
                .foregroundColor(.secondary)
            Text("No Devices Found")
                .font(.headline)
            Text("Tap Scan to search for OBD adapters")
                .font(.subheadline)
                .foregroundColor(.secondary)
            Spacer()
        }
        .padding()
    }

    // MARK: - Toolbar

    private var scanButton: some View {
        Button {
            if viewModel.isScanning {
                viewModel.stopScanning()
            } else {
                viewModel.startScanning()
            }
        } label: {
            Text(viewModel.isScanning ? "Stop" : "Scan")
        }
    }

    // MARK: - Helpers

    private var statusColor: Color {
        switch viewModel.connectionState {
        case .disconnected: return .red
        case .scanning: return .orange
        case .connecting: return .yellow
        case .connected, .ready: return .green
        }
    }

    private var statusText: String {
        switch viewModel.connectionState {
        case .disconnected: return "Disconnected"
        case .scanning: return "Scanning..."
        case .connecting:
            if viewModel.isAutoConnecting,
               let name = UserDefaults.standard.string(forKey: BluetoothManager.lastOBDDeviceNameKey) {
                return "Auto-connecting to \(name)..."
            }
            return "Connecting..."
        case .connected: return "Connected"
        case .ready: return "Ready"
        }
    }

    private func signalDescription(rssi: Int) -> String {
        switch rssi {
        case -50...0: return "Excellent signal"
        case -65...(-51): return "Good signal"
        case -80...(-66): return "Fair signal"
        default: return "Weak signal"
        }
    }

    private func signalBars(rssi: Int) -> some View {
        HStack(spacing: 2) {
            ForEach(0..<4) { bar in
                RoundedRectangle(cornerRadius: 1)
                    .fill(barActive(bar: bar, rssi: rssi) ? Color.green : Color.gray.opacity(0.3))
                    .frame(width: 4, height: CGFloat(6 + bar * 4))
            }
        }
        .frame(height: 20, alignment: .bottom)
    }

    private func barActive(bar: Int, rssi: Int) -> Bool {
        let thresholds = [-80, -65, -50, -35]
        return rssi >= thresholds[bar]
    }
}
