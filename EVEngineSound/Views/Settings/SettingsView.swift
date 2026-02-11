import SwiftUI

struct SettingsView: View {
    @EnvironmentObject private var coordinator: AppCoordinator
    @StateObject private var viewModel = SettingsViewModel()

    var body: some View {
        NavigationView {
            List {
                // Auto-connect
                Section {
                    Toggle("Auto Connect", isOn: $viewModel.autoConnectEnabled)
                } header: {
                    Text("Bluetooth")
                } footer: {
                    if let name = UserDefaults.standard.string(forKey: BluetoothManager.lastOBDDeviceNameKey) {
                        Text("Last device: \(name)")
                    }
                }

                // Profile selection
                Section {
                    ForEach(AudioProfile.allProfiles) { profile in
                        Button {
                            viewModel.selectedProfile = profile
                        } label: {
                            HStack {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(profile.name)
                                        .font(.body)
                                        .foregroundColor(.primary)
                                    Text("RPM: \(Int(profile.minRPM)) - \(Int(profile.maxRPM))")
                                        .font(.caption)
                                        .foregroundColor(.secondary)
                                }
                                Spacer()
                                if viewModel.selectedProfile == profile {
                                    Image(systemName: "checkmark")
                                        .foregroundColor(.accentColor)
                                        .fontWeight(.semibold)
                                }
                            }
                        }
                    }
                } header: {
                    Text("Sound Profile")
                }

                // Volume control
                Section {
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Text("Master Volume")
                            Spacer()
                            Text("\(Int(viewModel.masterVolume * 100))%")
                                .monospacedDigit()
                                .foregroundColor(.secondary)
                        }
                        Slider(value: $viewModel.masterVolume, in: 0...1, step: 0.01)
                            .tint(.accentColor)
                    }
                } header: {
                    Text("Audio")
                }

                // App info
                Section {
                    HStack {
                        Text("Version")
                        Spacer()
                        Text(appVersion)
                            .foregroundColor(.secondary)
                    }
                    HStack {
                        Text("Build")
                        Spacer()
                        Text(buildNumber)
                            .foregroundColor(.secondary)
                    }
                } header: {
                    Text("About")
                } footer: {
                    Text("EVEngineSound simulates engine sounds for electric vehicles using real OBD-II data or demo mode.")
                        .font(.caption)
                }
            }
            .navigationTitle("Settings")
            .onAppear {
                viewModel.bind(to: coordinator)
            }
        }
    }

    // MARK: - Helpers

    private var appVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.0"
    }

    private var buildNumber: String {
        Bundle.main.infoDictionary?["CFBundleVersion"] as? String ?? "1"
    }
}

#Preview {
    SettingsView()
        .environmentObject(AppCoordinator())
}
