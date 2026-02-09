import SwiftUI

struct DebugView: View {
    @EnvironmentObject private var coordinator: AppCoordinator
    @ObservedObject var logger: OBDLogger

    var body: some View {
        NavigationView {
            VStack(spacing: 0) {
                // Connection state banner (driven by coordinator's @Published bleState)
                connectionBanner

                // Log entries
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 2) {
                            ForEach(logger.entries) { entry in
                                logLine(entry)
                                    .id(entry.id)
                            }
                        }
                        .padding(.horizontal, 8)
                        .padding(.vertical, 4)
                    }
                    .background(Color.black)
                    .onChange(of: logger.entries.count) { _ in
                        if let last = logger.entries.last {
                            withAnimation {
                                proxy.scrollTo(last.id, anchor: .bottom)
                            }
                        }
                    }
                }

                if logger.entries.isEmpty {
                    Spacer()
                    Text("No log entries yet.\nConnect a BLE OBD adapter to see data.")
                        .font(.caption)
                        .foregroundColor(.gray)
                        .multilineTextAlignment(.center)
                        .padding()
                    Spacer()
                }
            }
            .navigationTitle("Debug Console")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Clear") {
                        logger.clear()
                    }
                }
            }
        }
    }

    private var connectionBanner: some View {
        HStack {
            Circle()
                .fill(bannerColor)
                .frame(width: 10, height: 10)
            Text(bannerText)
                .font(.caption)
                .fontWeight(.medium)
            Spacer()
            Text("\(logger.entries.count) entries")
                .font(.caption2)
                .foregroundColor(.secondary)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(bannerColor.opacity(0.15))
    }

    private var bannerColor: Color {
        switch coordinator.bleState {
        case .ready: return .green
        case .connected: return .yellow
        case .connecting, .scanning: return .orange
        case .disconnected: return .red
        }
    }

    private var bannerText: String {
        switch coordinator.bleState {
        case .ready: return "BLE Ready"
        case .connected: return "BLE Connected (discovering services…)"
        case .connecting: return "Connecting…"
        case .scanning: return "Scanning…"
        case .disconnected: return "Disconnected"
        }
    }

    private func logLine(_ entry: OBDLogger.LogEntry) -> some View {
        let formatter = Self.timeFormatter
        let ts = formatter.string(from: entry.timestamp)
        let prefix: String
        let color: Color

        switch entry.direction {
        case .sent:
            prefix = "TX"
            color = .blue
        case .received:
            prefix = "RX"
            color = .green
        case .parsed:
            prefix = "OK"
            color = .orange
        case .error:
            prefix = "!!"
            color = .red
        }

        return Text("\(ts) [\(prefix)] \(entry.message)")
            .font(.system(size: 12, design: .monospaced))
            .foregroundColor(color)
            .frame(maxWidth: .infinity, alignment: .leading)
    }

    private static let timeFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss.SSS"
        return f
    }()
}
