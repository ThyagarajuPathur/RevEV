import Foundation
import Combine

final class DemoViewModel: ObservableObject {
    @Published var rpm: Double = 800
    @Published var pedalPosition: Double = 0
    @Published var selectedProfile: AudioProfile = .ferrari458
    @Published var isAutoRevving: Bool = false

    private let audioEngine = EngineAudioEngine()
    private var cancellables = Set<AnyCancellable>()
    private var autoRevTimer: Timer?

    // Auto-rev state
    private var autoRevGoingUp = true
    private let autoRevStep: Double = 80
    private let autoRevInterval: TimeInterval = 0.03

    init() {
        // When RPM slider changes, update audio engine
        $rpm
            .removeDuplicates()
            .sink { [weak self] newRPM in
                guard let self else { return }
                self.audioEngine.update(rpm: newRPM, pedalPosition: self.pedalPosition / 100.0)
            }
            .store(in: &cancellables)

        // When pedal slider changes, update audio engine
        $pedalPosition
            .removeDuplicates()
            .sink { [weak self] newPedal in
                guard let self else { return }
                self.audioEngine.update(rpm: self.rpm, pedalPosition: newPedal / 100.0)
            }
            .store(in: &cancellables)

        // When profile changes, switch audio engine profile
        $selectedProfile
            .dropFirst()
            .removeDuplicates()
            .sink { [weak self] profile in
                self?.audioEngine.switchProfile(to: profile)
            }
            .store(in: &cancellables)
    }

    func start() {
        audioEngine.start(with: selectedProfile)
        audioEngine.update(rpm: rpm, pedalPosition: pedalPosition / 100.0)
    }

    func stop() {
        stopAutoRev()
        audioEngine.stop()
    }

    // MARK: - Auto Rev

    func toggleAutoRev() {
        if isAutoRevving {
            stopAutoRev()
        } else {
            startAutoRev()
        }
    }

    private func startAutoRev() {
        isAutoRevving = true
        autoRevGoingUp = true
        pedalPosition = 80

        autoRevTimer = Timer.scheduledTimer(withTimeInterval: autoRevInterval, repeats: true) { [weak self] _ in
            guard let self else { return }
            DispatchQueue.main.async {
                self.stepAutoRev()
            }
        }
    }

    private func stopAutoRev() {
        autoRevTimer?.invalidate()
        autoRevTimer = nil
        isAutoRevving = false
    }

    private func stepAutoRev() {
        let idleRPM = selectedProfile.idleRPM
        let maxRPM = selectedProfile.maxRPM

        if autoRevGoingUp {
            rpm += autoRevStep
            if rpm >= maxRPM {
                rpm = maxRPM
                autoRevGoingUp = false
                pedalPosition = 10
            }
        } else {
            rpm -= autoRevStep * 0.6
            if rpm <= idleRPM {
                rpm = idleRPM
                autoRevGoingUp = true
                pedalPosition = 80
            }
        }
    }
}
