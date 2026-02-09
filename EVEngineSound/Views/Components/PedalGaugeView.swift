import SwiftUI

struct PedalGaugeView: View {
    let pedalPosition: Double

    private var normalizedPosition: Double {
        pedalPosition.clamped(to: 0...100) / 100.0
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Pedal")
                    .font(.subheadline)
                    .fontWeight(.semibold)
                    .foregroundColor(.secondary)
                Spacer()
                Text("\(Int(pedalPosition))%")
                    .font(.subheadline)
                    .fontWeight(.bold)
                    .monospacedDigit()
            }

            GeometryReader { geometry in
                ZStack(alignment: .leading) {
                    // Track background
                    RoundedRectangle(cornerRadius: 8)
                        .fill(Color(.systemGray5))

                    // Fill bar with gradient
                    RoundedRectangle(cornerRadius: 8)
                        .fill(
                            LinearGradient(
                                gradient: Gradient(colors: [.gaugeGreen, .gaugeYellow, .gaugeRed]),
                                startPoint: .leading,
                                endPoint: .trailing
                            )
                        )
                        .frame(width: geometry.size.width * normalizedPosition)
                        .animation(.spring(response: 0.3, dampingFraction: 0.8), value: pedalPosition)
                }
            }
            .frame(height: 24)
        }
        .padding(.horizontal)
    }
}

#Preview {
    VStack(spacing: 20) {
        PedalGaugeView(pedalPosition: 0)
        PedalGaugeView(pedalPosition: 50)
        PedalGaugeView(pedalPosition: 100)
    }
    .padding()
}
