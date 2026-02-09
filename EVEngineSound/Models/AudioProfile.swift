import Foundation

/// Defines an engine sound profile with sample layers and RPM anchors
struct AudioProfile: Identifiable, Hashable {
    let id: String
    let name: String
    let layers: [AudioLayer]
    let minRPM: Double
    let maxRPM: Double
    let idleRPM: Double
    let softLimiterRPM: Double
    let limiterRPM: Double

    func hash(into hasher: inout Hasher) {
        hasher.combine(id)
    }

    static func == (lhs: AudioProfile, rhs: AudioProfile) -> Bool {
        lhs.id == rhs.id
    }
}

/// A single audio layer within a profile
struct AudioLayer: Identifiable {
    let id: String
    let filename: String
    let folder: String
    let anchorRPM: Double
    let type: LayerType

    /// Full resource path within the bundle
    var resourcePath: String { "\(folder)/\(filename)" }

    enum LayerType {
        case onLow
        case onHigh
        case offLow
        case offHigh
        case limiter
        case extra
    }
}

/// Built-in audio profiles
extension AudioProfile {
    static let ferrari458 = AudioProfile(
        id: "458",
        name: "Ferrari 458",
        layers: [
            AudioLayer(id: "458_on_high", filename: "on_high", folder: "458", anchorRPM: 7900, type: .onHigh),
            AudioLayer(id: "458_on_higher", filename: "on_higher", folder: "458", anchorRPM: 7900, type: .onHigh),
            AudioLayer(id: "458_power", filename: "power_2", folder: "458", anchorRPM: 5300, type: .onLow),
            AudioLayer(id: "458_off_higher", filename: "off_higher", folder: "458", anchorRPM: 7900, type: .offHigh),
            AudioLayer(id: "458_off_midhigh", filename: "off_midhigh", folder: "458", anchorRPM: 5300, type: .offLow),
            AudioLayer(id: "458_mid_res", filename: "mid_res_2", folder: "458", anchorRPM: 5300, type: .extra),
            AudioLayer(id: "458_limiter", filename: "limiter", folder: "458", anchorRPM: 7900, type: .limiter),
        ],
        minRPM: 800,
        maxRPM: 9000,
        idleRPM: 800,
        softLimiterRPM: 8200,
        limiterRPM: 9000
    )

    static let procar = AudioProfile(
        id: "procar",
        name: "Procar",
        layers: [
            AudioLayer(id: "procar_on_high", filename: "on_high", folder: "procar", anchorRPM: 8430, type: .onHigh),
            AudioLayer(id: "procar_on_midhigh", filename: "on_midhigh", folder: "procar", anchorRPM: 5900, type: .onLow),
            AudioLayer(id: "procar_on_low", filename: "on_low", folder: "procar", anchorRPM: 3200, type: .onLow),
            AudioLayer(id: "procar_off_midhigh", filename: "off_midhigh", folder: "procar", anchorRPM: 5900, type: .offHigh),
            AudioLayer(id: "procar_off_lower", filename: "off_lower", folder: "procar", anchorRPM: 3200, type: .offLow),
        ],
        minRPM: 800,
        maxRPM: 9000,
        idleRPM: 800,
        softLimiterRPM: 8000,
        limiterRPM: 9000
    )

    static let bacMono = AudioProfile(
        id: "BAC_Mono",
        name: "BAC Mono",
        layers: [
            AudioLayer(id: "bac_on_high", filename: "BAC_Mono_onhigh", folder: "BAC_Mono", anchorRPM: 5000, type: .onHigh),
            AudioLayer(id: "bac_on_mid", filename: "BAC_Mono_onmid", folder: "BAC_Mono", anchorRPM: 3000, type: .onLow),
            AudioLayer(id: "bac_on_low", filename: "BAC_Mono_onlow", folder: "BAC_Mono", anchorRPM: 1000, type: .onLow),
            AudioLayer(id: "bac_off_veryhigh", filename: "BAC_Mono_offveryhigh", folder: "BAC_Mono", anchorRPM: 7000, type: .offHigh),
            AudioLayer(id: "bac_off_high", filename: "BAC_Mono_offhigh", folder: "BAC_Mono", anchorRPM: 5000, type: .offHigh),
            AudioLayer(id: "bac_off_mid", filename: "BAC_Mono_offmid", folder: "BAC_Mono", anchorRPM: 3000, type: .offLow),
            AudioLayer(id: "bac_off_low", filename: "BAC_Mono_offlow", folder: "BAC_Mono", anchorRPM: 1000, type: .offLow),
            AudioLayer(id: "bac_limiter", filename: "limiter", folder: "BAC_Mono", anchorRPM: 7000, type: .limiter),
            AudioLayer(id: "bac_rev", filename: "REV", folder: "BAC_Mono", anchorRPM: 5000, type: .extra),
        ],
        minRPM: 800,
        maxRPM: 8500,
        idleRPM: 800,
        softLimiterRPM: 7500,
        limiterRPM: 8500
    )

    static let allProfiles: [AudioProfile] = [.ferrari458, .procar, .bacMono]
}
