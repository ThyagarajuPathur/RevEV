import Foundation
import Combine

final class SettingsViewModel: ObservableObject {
    @Published var selectedProfile: AudioProfile = .ferrari458
    @Published var masterVolume: Double = 0.8

    private weak var coordinator: AppCoordinator?
    private var cancellables = Set<AnyCancellable>()

    func bind(to coordinator: AppCoordinator) {
        self.coordinator = coordinator

        // Sync profile from coordinator
        coordinator.$currentProfile
            .receive(on: DispatchQueue.main)
            .assign(to: &$selectedProfile)

        // Push profile changes back to coordinator
        $selectedProfile
            .dropFirst()
            .removeDuplicates()
            .sink { [weak coordinator] profile in
                coordinator?.selectProfile(profile)
            }
            .store(in: &cancellables)

        // Push volume changes to audio engine
        $masterVolume
            .dropFirst()
            .removeDuplicates()
            .sink { [weak coordinator] volume in
                coordinator?.audioEngine.masterVolume = Float(volume)
            }
            .store(in: &cancellables)
    }
}
