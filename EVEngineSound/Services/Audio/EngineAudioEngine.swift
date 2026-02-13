import AVFoundation
import Combine
import QuartzCore

/// Core audio engine that plays and crossfades layered engine sound samples.
///
/// Architecture per layer:
/// ```
/// AVAudioPlayerNode -> AVAudioUnitVarispeed -> MainMixer -> Output
/// ```
///
/// Two independent systems drive the audio, updated at ~60 Hz by a CADisplayLink:
///
/// 1. **RPM → Pitch**: Velocity extrapolation between OBD readings (~14 Hz)
///    fills the gaps with smooth, continuous RPM. Drift correction and output
///    EMA prevent divergence from real OBD values.
///
/// 2. **Pedal → Layer Balance**: Smoothed pedal position drives the throttle-axis
///    crossfade in RPMAudioMapper (on-layers vs off-layers). Equal-power cosine
///    crossfade keeps perceived volume constant during transitions.
///
/// The only coupling: pedal position scales extrapolation confidence. When the
/// pedal is released, confidence drops naturally, limiting RPM overshoot without
/// any special-case logic or intent detection.
final class EngineAudioEngine: ObservableObject {

    // MARK: - Published state

    @Published private(set) var isRunning = false
    @Published var masterVolume: Float = 1.0 {
        didSet { engine.mainMixerNode.outputVolume = masterVolume }
    }
    @Published private(set) var currentProfile: AudioProfile?

    // MARK: - Private properties

    private let audioEngine = AVAudioEngine()
    private let sampleManager = AudioSampleManager()
    private let mapper = RPMAudioMapper()

    /// Optional logger for debug visibility
    var logger: OBDLogger?

    /// Per-layer nodes
    private var playerNodes: [String: AVAudioPlayerNode] = [:]
    private var varispeedNodes: [String: AVAudioUnitVarispeed] = [:]
    private var buffers: [String: AVAudioPCMBuffer] = [:]

    /// Display link for 60 Hz update cadence
    private var displayLink: CADisplayLink?

    // MARK: - RPM Smoothing (independent of pedal)

    /// Rendered RPM — extrapolated at 60fps, fed to audio
    private var renderedRPM: Double = 0

    /// Final output RPM — one last smoothing pass before the mapper
    private var outputRPM: Double = 0
    private let outputSmoothing: Double = 0.12 // EMA alpha per frame

    /// Current RPM velocity (RPM/sec) — smoothed each frame toward target rate
    private var rpmRate: Double = 0
    /// Target velocity computed from OBD deltas
    private var targetRate: Double = 0
    /// How fast rpmRate chases targetRate each frame (lower = smoother velocity changes)
    private let rateSmoothingPerFrame: Double = 0.06

    /// Previous OBD reading for velocity calculation
    private var prevOBDRpm: Double = 0
    private var prevOBDTime: CFTimeInterval = 0

    /// Latest OBD target for gentle drift correction
    private var targetRPM: Double = 0

    /// Correction: gently steer rendered value toward actual OBD each frame
    private let correctionFactor: Double = 0.02

    // MARK: - Pedal Smoothing (independent of RPM)

    /// Raw pedal target from OBD (set at ~14Hz).
    private var targetPedal: Double = 0

    /// Smoothed pedal for audio — what the mapper sees for throttle-axis crossfade.
    private var renderedPedal: Double = 0

    /// Pedal smoothing rate (EMA alpha per frame at 60fps).
    /// 0.15/frame ≈ 100ms to settle — fast enough to feel responsive,
    /// slow enough to avoid a hard cut between on/off layers.
    private let pedalSmoothing: Double = 0.15

    /// Last display link timestamp
    private var lastTimestamp: CFTimeInterval = 0

    private var updateCount: Int = 0

    /// Concurrency guard
    private let updateQueue = DispatchQueue(label: "com.revev.audioengine.update")

    // MARK: - Convenience accessor

    private var engine: AVAudioEngine { audioEngine }

    // MARK: - Lifecycle

    init() {
        setupNotifications()
    }

    deinit {
        stop()
        NotificationCenter.default.removeObserver(self)
    }

    // MARK: - Public API

    /// Build the audio graph for the given profile and start playback.
    func start(with profile: AudioProfile) {
        stop()
        configureAudioSession()
        buildGraph(for: profile)
        currentProfile = profile

        logger?.logParsed("Audio: loaded \(buffers.count)/\(profile.layers.count) samples for \(profile.name)")
        if buffers.isEmpty {
            logger?.logError("Audio: NO samples loaded — check bundle")
        }

        do {
            try engine.start()
            startAllPlayers()
            startDisplayLink()
            isRunning = true
            logger?.logParsed("Audio: engine running, \(playerNodes.count) players active")
        } catch {
            logger?.logError("Audio: AVAudioEngine failed to start: \(error)")
            print("[EngineAudioEngine] Failed to start AVAudioEngine: \(error)")
        }
    }

    /// Stop engine and tear down graph.
    func stop() {
        stopDisplayLink()
        stopAllPlayers()
        tearDownGraph()
        engine.stop()
        isRunning = false
        lastTimestamp = 0
        rpmRate = 0
        targetRate = 0
        prevOBDTime = 0
        renderedRPM = 0
        targetPedal = 0
        renderedPedal = 0
        outputRPM = 0
    }

    /// Switch to a different sound profile without full teardown.
    func switchProfile(to profile: AudioProfile) {
        start(with: profile)
    }

    /// Called externally (e.g. from OBD at ~14Hz) to push new vehicle data.
    /// Simply stores the target values — all smoothing happens in the display link.
    func update(rpm: Double, pedalPosition: Double) {
        let now = CACurrentMediaTime()

        // Compute instantaneous velocity from consecutive OBD readings
        if prevOBDTime > 0 {
            let dt = now - prevOBDTime
            if dt > 0.01 {
                let instantRate = (rpm - prevOBDRpm) / dt
                targetRate = targetRate * 0.5 + instantRate * 0.5
            }
        }

        prevOBDRpm = rpm
        prevOBDTime = now
        targetRPM = rpm
        targetPedal = pedalPosition

        updateCount += 1
        if updateCount % 50 == 1 {
            logger?.logParsed("Audio: OBD=\(Int(rpm)) out=\(Int(outputRPM)) rate=\(Int(rpmRate))/s pedal=\(String(format: "%.0f%%", pedalPosition * 100))")
        }
    }

    // MARK: - Audio Session

    private func configureAudioSession() {
        let session = AVAudioSession.sharedInstance()
        do {
            try session.setCategory(.playback, mode: .default, options: [.mixWithOthers])
            try session.setPreferredIOBufferDuration(0.005)
            try session.setActive(true)
        } catch {
            print("[EngineAudioEngine] Audio session setup failed: \(error)")
        }
    }

    // MARK: - Graph Construction

    private func buildGraph(for profile: AudioProfile) {
        buffers = sampleManager.loadSamples(for: profile)

        let format = AudioSampleManager.standardFormat

        for layer in profile.layers {
            guard let buffer = buffers[layer.id] else { continue }

            let player = AVAudioPlayerNode()
            let varispeed = AVAudioUnitVarispeed()

            engine.attach(player)
            engine.attach(varispeed)

            // Use the buffer's format for the connection from player to varispeed
            let bufferFormat = buffer.format

            engine.connect(player, to: varispeed, format: bufferFormat)
            engine.connect(varispeed, to: engine.mainMixerNode, format: format)

            playerNodes[layer.id] = player
            varispeedNodes[layer.id] = varispeed
        }

        engine.mainMixerNode.outputVolume = masterVolume
    }

    private func tearDownGraph() {
        for (_, player) in playerNodes {
            engine.detach(player)
        }
        for (_, varispeed) in varispeedNodes {
            engine.detach(varispeed)
        }
        playerNodes.removeAll()
        varispeedNodes.removeAll()
        buffers.removeAll()
    }

    // MARK: - Player Control

    private func startAllPlayers() {
        guard let profile = currentProfile else { return }
        for layer in profile.layers {
            guard let player = playerNodes[layer.id],
                  let buffer = buffers[layer.id] else { continue }
            // Start with zero volume; the update loop will ramp it
            player.volume = 0
            player.scheduleBuffer(buffer, at: nil, options: .loops)
            player.play()
        }
    }

    private func stopAllPlayers() {
        for (_, player) in playerNodes {
            player.stop()
        }
    }

    // MARK: - Display Link (60 Hz update)

    private func startDisplayLink() {
        stopDisplayLink()
        let link = CADisplayLink(target: self, selector: #selector(displayLinkFired))
        link.preferredFrameRateRange = CAFrameRateRange(
            minimum: 30,
            maximum: 60,
            preferred: 60
        )
        link.add(to: .main, forMode: .common)
        displayLink = link
    }

    private func stopDisplayLink() {
        displayLink?.invalidate()
        displayLink = nil
    }

    @objc private func displayLinkFired(_ link: CADisplayLink) {
        guard let profile = currentProfile else { return }

        // Compute delta time (first frame gets a sensible default)
        let dt: Double
        if lastTimestamp == 0 {
            dt = 1.0 / 60.0
        } else {
            dt = min(link.timestamp - lastTimestamp, 0.1) // cap at 100ms
        }
        lastTimestamp = link.timestamp

        // ── Pedal smoothing (runs first — drives RPM responsiveness) ──

        renderedPedal += (targetPedal - renderedPedal) * pedalSmoothing

        // ── RPM smoothing (adaptive based on gap + pedal) ─────────────

        let rpmGap = abs(targetRPM - renderedRPM)

        // Pedal-reactive: when pedal is high, track faster across all stages
        let pedalUrgency = renderedPedal.clamped(to: 0...1) // 0 = coasting, 1 = full throttle
        let urgencyBoost = 1.0 + pedalUrgency * 2.0         // 1x–3x multiplier

        // 1. Smoothly steer rpmRate toward targetRate
        //    Base 0.06 → up to 0.18 at full throttle; jumps to 0.30 on large gaps
        let adaptiveRateSmoothing = (rpmGap > 500 ? 0.30 : rateSmoothingPerFrame * urgencyBoost)
        rpmRate += (targetRate - rpmRate) * adaptiveRateSmoothing

        // 2. Extrapolate RPM, scaled by pedal confidence.
        let confidence = max(renderedPedal, 0.05)
        renderedRPM += rpmRate * dt * confidence

        // 3. Adaptive drift correction toward actual OBD value
        //    Small gaps (<500 RPM): gentle 2%–6% correction (smooth interpolation)
        //    Large gaps (>500 RPM): aggressive 15%–25% correction (snap to reality)
        let adaptiveCorrection: Double
        if rpmGap > 500 {
            adaptiveCorrection = (0.15 + 0.10 * pedalUrgency)
        } else {
            adaptiveCorrection = correctionFactor * urgencyBoost
        }
        renderedRPM += (targetRPM - renderedRPM) * adaptiveCorrection

        // 4. Clamp
        renderedRPM = max(0, renderedRPM)

        // 5. Final output smoothing — also pedal-reactive
        //    Base 0.12 → up to 0.36 at full throttle; 0.50 on large gaps
        let adaptiveOutputSmoothing = (rpmGap > 300 ? 0.50 : outputSmoothing * urgencyBoost)
        outputRPM += (renderedRPM - outputRPM) * adaptiveOutputSmoothing

        // ── Apply to audio ───────────────────────────────────────────

        let params = mapper.calculateLayerParams(
            rpm: outputRPM,
            pedalPosition: renderedPedal,
            profile: profile
        )

        for p in params {
            playerNodes[p.layerId]?.volume = p.gain
            varispeedNodes[p.layerId]?.rate = p.playbackRate
        }
    }


    // MARK: - Interruption Handling

    private func setupNotifications() {
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(handleInterruption),
            name: AVAudioSession.interruptionNotification,
            object: AVAudioSession.sharedInstance()
        )
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(handleRouteChange),
            name: AVAudioSession.routeChangeNotification,
            object: AVAudioSession.sharedInstance()
        )
    }

    @objc private func handleInterruption(_ notification: Notification) {
        guard let info = notification.userInfo,
              let typeValue = info[AVAudioSessionInterruptionTypeKey] as? UInt,
              let type = AVAudioSession.InterruptionType(rawValue: typeValue) else {
            return
        }

        switch type {
        case .began:
            // Engine pauses automatically; stop display link
            stopDisplayLink()
            isRunning = false

        case .ended:
            guard let optionsValue = info[AVAudioSessionInterruptionOptionKey] as? UInt else {
                return
            }
            let options = AVAudioSession.InterruptionOptions(rawValue: optionsValue)
            if options.contains(.shouldResume) {
                do {
                    try engine.start()
                    startAllPlayers()
                    startDisplayLink()
                    isRunning = true
                } catch {
                    print("[EngineAudioEngine] Failed to resume after interruption: \(error)")
                }
            }

        @unknown default:
            break
        }
    }

    @objc private func handleRouteChange(_ notification: Notification) {
        guard let info = notification.userInfo,
              let reasonValue = info[AVAudioSessionRouteChangeReasonKey] as? UInt,
              let reason = AVAudioSession.RouteChangeReason(rawValue: reasonValue) else {
            return
        }

        // If headphones were unplugged, pause playback
        if reason == .oldDeviceUnavailable {
            stop()
        }
    }
}
