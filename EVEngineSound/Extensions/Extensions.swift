import SwiftUI

// MARK: - Data+Hex

extension Data {
    /// Hex string representation of the data (lowercase)
    var hexString: String {
        map { String(format: "%02x", $0) }.joined()
    }

    /// Initialize Data from a hex string (e.g. "48656C6C6F")
    init?(hex: String) {
        let cleanHex = hex.replacingOccurrences(of: " ", with: "")
        guard cleanHex.count.isMultiple(of: 2) else { return nil }
        var data = Data(capacity: cleanHex.count / 2)
        var index = cleanHex.startIndex
        while index < cleanHex.endIndex {
            let nextIndex = cleanHex.index(index, offsetBy: 2)
            guard let byte = UInt8(cleanHex[index..<nextIndex], radix: 16) else {
                return nil
            }
            data.append(byte)
            index = nextIndex
        }
        self = data
    }
}

// MARK: - Color Helpers

extension Color {
    /// Gauge arc segment colors
    static let gaugeGreen = Color(red: 0.2, green: 0.8, blue: 0.3)
    static let gaugeYellow = Color(red: 0.95, green: 0.8, blue: 0.1)
    static let gaugeOrange = Color(red: 0.95, green: 0.5, blue: 0.1)
    static let gaugeRed = Color(red: 0.9, green: 0.15, blue: 0.15)
}

// MARK: - View Modifiers

struct CardStyle: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding()
            .background(Color(.systemBackground))
            .cornerRadius(16)
            .shadow(color: Color.black.opacity(0.1), radius: 8, x: 0, y: 2)
    }
}

struct SectionHeaderStyle: ViewModifier {
    func body(content: Content) -> some View {
        content
            .font(.headline)
            .foregroundColor(.secondary)
            .textCase(.uppercase)
            .padding(.horizontal)
    }
}

extension View {
    func cardStyle() -> some View {
        modifier(CardStyle())
    }

    func sectionHeaderStyle() -> some View {
        modifier(SectionHeaderStyle())
    }
}

// MARK: - Double+Clamped

extension Double {
    func clamped(to range: ClosedRange<Double>) -> Double {
        Swift.min(Swift.max(self, range.lowerBound), range.upperBound)
    }
}
