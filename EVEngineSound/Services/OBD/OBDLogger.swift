import Foundation
import Combine

/// Centralized debug logger for OBD communication
final class OBDLogger: ObservableObject {

    enum Direction: String {
        case sent, received, parsed, error
    }

    struct LogEntry: Identifiable {
        let id = UUID()
        let timestamp: Date
        let direction: Direction
        let message: String
    }

    @Published private(set) var entries: [LogEntry] = []

    private let maxEntries = 500

    func logSent(_ message: String) {
        append(.sent, message)
    }

    func logReceived(_ message: String) {
        append(.received, message)
    }

    func logParsed(_ message: String) {
        append(.parsed, message)
    }

    func logError(_ message: String) {
        append(.error, message)
    }

    func clear() {
        entries.removeAll()
    }

    private func append(_ direction: Direction, _ message: String) {
        let entry = LogEntry(timestamp: Date(), direction: direction, message: message)
        if Thread.isMainThread {
            addEntry(entry)
        } else {
            DispatchQueue.main.async { [weak self] in
                self?.addEntry(entry)
            }
        }
    }

    private func addEntry(_ entry: LogEntry) {
        entries.append(entry)
        if entries.count > maxEntries {
            entries.removeFirst(entries.count - maxEntries)
        }
    }
}
