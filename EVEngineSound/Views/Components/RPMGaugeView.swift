import SwiftUI

struct RPMGaugeView: View {
    let rpm: Double
    let maxRPM: Double

    private let startAngle: Double = 135
    private let endAngle: Double = 405
    private let sweepAngle: Double = 270

    private var normalizedRPM: Double {
        rpm.clamped(to: 0...maxRPM) / maxRPM
    }

    private var needleAngle: Angle {
        .degrees(startAngle + sweepAngle * normalizedRPM)
    }

    var body: some View {
        GeometryReader { geometry in
            let size = min(geometry.size.width, geometry.size.height)
            let center = CGPoint(x: geometry.size.width / 2, y: geometry.size.height / 2)
            let radius = size * 0.4
            let lineWidth: CGFloat = size * 0.06

            ZStack {
                // Background arc
                arcPath(radius: radius, from: startAngle, to: endAngle)
                    .stroke(Color(.systemGray5), style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))

                // Colored segments (proportional to maxRPM)
                arcSegment(radius: radius, lineWidth: lineWidth,
                           segmentStart: 0, segmentEnd: 0.4,
                           color: .gaugeGreen)

                arcSegment(radius: radius, lineWidth: lineWidth,
                           segmentStart: 0.4, segmentEnd: 0.7,
                           color: .gaugeYellow)

                arcSegment(radius: radius, lineWidth: lineWidth,
                           segmentStart: 0.7, segmentEnd: 0.88,
                           color: .gaugeOrange)

                arcSegment(radius: radius, lineWidth: lineWidth,
                           segmentStart: 0.88, segmentEnd: 1.0,
                           color: .gaugeRed)

                // Minor ticks (every 500 RPM)
                ForEach(0..<Int(maxRPM / 500), id: \.self) { tick in
                    let rpmValue = Double(tick) * 500.0
                    if rpmValue.truncatingRemainder(dividingBy: 1000) != 0 {
                        let fraction = rpmValue / maxRPM
                        let angle = startAngle + sweepAngle * fraction
                        tickMark(center: center, radius: radius, angle: angle,
                                 length: size * 0.03, lineWidth: 1)
                    }
                }

                // Major ticks (every 1000 RPM) + labels
                ForEach(0...Int(maxRPM / 1000), id: \.self) { tick in
                    let fraction = Double(tick) * 1000.0 / maxRPM
                    let angle = startAngle + sweepAngle * fraction
                    tickMark(center: center, radius: radius, angle: angle,
                             length: size * 0.06, lineWidth: 2)

                    tickLabel(tick: tick, center: center, radius: radius - size * 0.15,
                              angle: angle, fontSize: size * 0.05)
                }

                // Needle
                NeedleShape()
                    .fill(Color.gaugeRed)
                    .frame(width: size * 0.02, height: radius * 0.75)
                    .offset(y: -radius * 0.75 / 2)
                    .rotationEffect(needleAngle, anchor: .bottom)
                    .animation(.spring(response: 0.4, dampingFraction: 0.7), value: rpm)

                // Center cap
                Circle()
                    .fill(Color(.systemGray2))
                    .frame(width: size * 0.06, height: size * 0.06)

                // RPM display
                VStack(spacing: 2) {
                    Text("\(Int(rpm))")
                        .font(.system(size: size * 0.12, weight: .bold, design: .rounded))
                        .monospacedDigit()
                    Text("RPM")
                        .font(.system(size: size * 0.04, weight: .medium))
                        .foregroundColor(.secondary)
                }
                .offset(y: size * 0.18)

            }
            .position(center)
        }
        .aspectRatio(1, contentMode: .fit)
    }

    // MARK: - Helpers

    private func arcPath(radius: CGFloat, from: Double, to: Double) -> Path {
        Path { path in
            path.addArc(center: .zero, radius: radius,
                        startAngle: .degrees(from - 90),
                        endAngle: .degrees(to - 90),
                        clockwise: false)
        }
        .offsetBy(dx: 0, dy: 0)
    }

    @ViewBuilder
    private func arcSegment(radius: CGFloat, lineWidth: CGFloat,
                            segmentStart: Double, segmentEnd: Double,
                            color: Color) -> some View {
        let from = startAngle + sweepAngle * segmentStart
        let to = startAngle + sweepAngle * min(segmentEnd, 1.0)
        Path { path in
            path.addArc(center: .zero, radius: radius,
                        startAngle: .degrees(from - 90),
                        endAngle: .degrees(to - 90),
                        clockwise: false)
        }
        .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .butt))
    }

    @ViewBuilder
    private func tickMark(center: CGPoint, radius: CGFloat, angle: Double,
                          length: CGFloat, lineWidth: CGFloat) -> some View {
        let rad = (angle - 90) * .pi / 180
        Path { path in
            let cosVal = CGFloat(cos(rad))
            let sinVal = CGFloat(sin(rad))
            let outerPoint = CGPoint(x: cosVal * (radius + length / 2),
                                     y: sinVal * (radius + length / 2))
            let innerPoint = CGPoint(x: cosVal * (radius - length / 2),
                                     y: sinVal * (radius - length / 2))
            path.move(to: outerPoint)
            path.addLine(to: innerPoint)
        }
        .stroke(Color(.label), lineWidth: lineWidth)
    }

    @ViewBuilder
    private func tickLabel(tick: Int, center: CGPoint, radius: CGFloat,
                           angle: Double, fontSize: CGFloat) -> some View {
        let rad = (angle - 90) * .pi / 180
        Text("\(tick)")
            .font(.system(size: fontSize, weight: .medium, design: .rounded))
            .foregroundColor(.secondary)
            .position(x: CGFloat(cos(rad)) * radius + center.x,
                      y: CGFloat(sin(rad)) * radius + center.y)
    }

}

// MARK: - Needle Shape

private struct NeedleShape: Shape {
    func path(in rect: CGRect) -> Path {
        Path { path in
            let width = rect.width
            let height = rect.height
            path.move(to: CGPoint(x: rect.midX - width / 2, y: height))
            path.addLine(to: CGPoint(x: rect.midX, y: 0))
            path.addLine(to: CGPoint(x: rect.midX + width / 2, y: height))
            path.closeSubpath()
        }
    }
}

#Preview {
    RPMGaugeView(rpm: 4500, maxRPM: 9000)
        .frame(width: 300, height: 300)
        .padding()
}
