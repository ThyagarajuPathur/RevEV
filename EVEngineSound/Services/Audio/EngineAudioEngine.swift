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
/// A CADisplayLink at ~60 Hz drives the update loop that applies gain
/// and playback-rate changes calculated by RPMAudioMapper.
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

    /// Rendered RPM — extrapolated at 60fps, fed to audio
    private var renderedRPM: Double = 0
    private var renderedPedal: Double = 0

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
        renderedPedal = 0
        outputRPM = 0
    }

    /// Switch to a different sound profile without full teardown.
    func switchProfile(to profile: AudioProfile) {
        start(with: profile)
    }

    /// Called externally (e.g. from OBD at ~14Hz) to push new vehicle data.
    /// Computes target velocity — actual rpmRate smooths toward it each frame.
    func update(rpm: Double, pedalPosition: Double) {
        let now = CACurrentMediaTime()

        // Compute instantaneous velocity from consecutive OBD readings
        if prevOBDTime > 0 {
            let dt = now - prevOBDTime
            if dt > 0.01 {
                let instantRate = (rpm - prevOBDRpm) / dt
                // Blend into target rate (not rpmRate directly — that smooths per-frame)
                targetRate = targetRate * 0.5 + instantRate * 0.5
            }
        }

        prevOBDRpm = rpm
        prevOBDTime = now
        targetRPM = rpm
        renderedPedal = 1.0 // Hardcoded to 100% throttle

        updateCount += 1
        if updateCount % 50 == 1 {
            logger?.logParsed("Audio: OBD=\(Int(rpm)) out=\(Int(outputRPM)) rate=\(Int(rpmRate))/s running=\(isRunning)")
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

        // 1. Smoothly steer rpmRate toward targetRate (no sudden velocity changes)
        rpmRate += (targetRate - rpmRate) * rateSmoothingPerFrame

        // 2. Extrapolate using smooth velocity
        renderedRPM += rpmRate * dt

        // 3. Gently correct drift toward actual OBD value
        renderedRPM += (targetRPM - renderedRPM) * correctionFactor

        // 4. Clamp
        renderedRPM = max(0, renderedRPM)

        // 5. Final output smoothing — catches any remaining micro-kinks
        outputRPM += (renderedRPM - outputRPM) * outputSmoothing

        let rpm = outputRPM
        let pedal = renderedPedal

        let params = mapper.calculateLayerParams(
            rpm: rpm,
            pedalPosition: pedal,
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
