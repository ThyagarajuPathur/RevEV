import Foundation
import Combine

final class DashboardViewModel: ObservableObject {
    @Published var rpm: Double = 0
    @Published var pedalPosition: Double = 0
    @Published var isConnected: Bool = false
    @Published var selectedProfile: AudioProfile = .ferrari458

    private var cancellables = Set<AnyCancellable>()

    func bind(to coordinator: AppCoordinator) {
        // Cancel any previous subscriptions (bind may be called again on re-appear)
        cancellables.removeAll()

        // Subscribe to OBD vehicle data for RPM/pedal
        coordinator.obdService.vehicleDataPublisher
            .receive(on: DispatchQueue.main)
            .sink { [weak self] data in
                self?.rpm = data.rpm
                self?.pedalPosition = data.pedalPosition * 100 // convert 0-1 to 0-100
            }
            .store(in: &cancellables)

        // Show connected as soon as BLE is connected or ready
        // Uses coordinator's @Published bleState for reactive updates
        coordinator.$bleState
            .receive(on: DispatchQueue.main)
            .sink { [weak self] state in
                self?.isConnected = (state == .connected || state == .ready)
            }
            .store(in: &cancellables)

        // Sync profile from coordinator
        coordinator.$currentProfile
            .receive(on: DispatchQueue.main)
            .assign(to: &$selectedProfile)
    }
}
