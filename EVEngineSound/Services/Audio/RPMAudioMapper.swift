import Foundation

/// Parameters for a single audio layer output by the mapper
struct LayerParams {
    let layerId: String
    let gain: Float
    let playbackRate: Float
}

/// Maps RPM and pedal position to per-layer gain and playback rate
/// using equal-power cosine crossfade ported from the engine-audio JS reference.
struct RPMAudioMapper {

    // MARK: - Public API

    /// Calculate gain and playback rate for every layer in the profile.
    func calculateLayerParams(
        rpm: Double,
        pedalPosition: Double,
        profile: AudioProfile
    ) -> [LayerParams] {

        // --- Gather anchor RPMs by type ---
        let lowAnchorRPM = anchorRPM(for: .low, in: profile)
        let highAnchorRPM = anchorRPM(for: .high, in: profile)

        // --- RPM axis crossfade (equal-power cosine) ---
        let t = clamp((rpm - lowAnchorRPM) / (highAnchorRPM - lowAnchorRPM), min: 0, max: 1)
        let lowGain = Float(cos(t * .pi / 2))
        let highGain = Float(sin(t * .pi / 2))

        // --- Throttle axis crossfade (equal-power cosine, matches RPM axis) ---
        // Linear crossfade (old: onGain + offGain = 1) causes a -3dB energy dip
        // at the midpoint. Equal-power (sin² + cos² = 1) keeps constant perceived
        // volume through the entire on→off transition, eliminating the audible
        // break when lifting off the accelerator.
        let pedalClamped = clamp(max(pedalPosition, 0.05), min: 0, max: 1)
        let onGain = Float(sin(pedalClamped * .pi / 2))
        let offGain = Float(cos(pedalClamped * .pi / 2))

        // --- Combined layer gains ---
        let onLow  = onGain * lowGain
        let onHigh = onGain * highGain
        let offLow  = offGain * lowGain
        let offHigh = offGain * highGain

        // --- Limiter ---
        let softLimiterStart = profile.softLimiterRPM * 0.93
        let limiterRatio = Float(clamp(
            (rpm - softLimiterStart) / (profile.limiterRPM - softLimiterStart),
            min: 0,
            max: 1
        ))

        // --- Build per-layer params ---
        return profile.layers.map { layer in
            let gain: Float
            switch layer.type {
            case .onLow:
                gain = onLow
            case .onHigh:
                gain = onHigh
            case .offLow:
                gain = offLow
            case .offHigh:
                gain = offHigh
            case .limiter:
                gain = limiterRatio
            case .extra:
                // Extra layers get a moderate blend of on-axis gain
                gain = onGain * 0.5
            }

            let rate = playbackRate(rpm: rpm, anchorRPM: layer.anchorRPM)
            return LayerParams(layerId: layer.id, gain: gain, playbackRate: rate)
        }
    }

    // MARK: - Helpers

    /// Playback rate = currentRPM / anchorRPM, clamped to [0.1, 4.0]
    func playbackRate(rpm: Double, anchorRPM: Double) -> Float {
        guard anchorRPM > 0 else { return 1.0 }
        let rate = rpm / anchorRPM
        return Float(clamp(rate, min: 0.1, max: 4.0))
    }

    /// Limiter ratio exposed for testing
    func limiterRatio(rpm: Double, profile: AudioProfile) -> Float {
        let softLimiterStart = profile.softLimiterRPM * 0.93
        return Float(clamp(
            (rpm - softLimiterStart) / (profile.limiterRPM - softLimiterStart),
            min: 0,
            max: 1
        ))
    }

    // MARK: - Private

    private enum AnchorBand {
        case low, high
    }

    /// Find a representative anchor RPM for low or high band from layers.
    private func anchorRPM(for band: AnchorBand, in profile: AudioProfile) -> Double {
        let types: [AudioLayer.LayerType]
        switch band {
        case .low:
            types = [.onLow, .offLow]
        case .high:
            types = [.onHigh, .offHigh]
        }
        let matching = profile.layers.filter { types.contains($0.type) }
        guard !matching.isEmpty else {
            return band == .low ? profile.minRPM : profile.maxRPM
        }
        // Use the average anchor RPM of matching layers
        let sum = matching.reduce(0.0) { $0 + $1.anchorRPM }
        return sum / Double(matching.count)
    }

    private func clamp(_ value: Double, min lo: Double, max hi: Double) -> Double {
        Swift.min(Swift.max(value, lo), hi)
    }
}
