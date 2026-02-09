import SwiftUI

@main
struct EVEngineSoundApp: App {
    @StateObject private var coordinator = AppCoordinator()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(coordinator)
        }
    }
}

/// Root view that holds the TabView. Needed so we can create ConnectionViewModel
/// from the coordinator's BluetoothManager via @StateObject.
private struct ContentView: View {
    @EnvironmentObject private var coordinator: AppCoordinator
    @State private var connectionViewModel: ConnectionViewModel?

    var body: some View {
        TabView {
            DashboardView()
                .tabItem {
                    Label("Dashboard", systemImage: "gauge.medium")
                }

            DemoView()
                .tabItem {
                    Label("Demo", systemImage: "play.circle")
                }

            if let vm = connectionViewModel {
                ConnectionView(viewModel: vm)
                    .tabItem {
                        Label("Connection", systemImage: "antenna.radiowaves.left.and.right")
                    }
            } else {
                ProgressView()
                    .tabItem {
                        Label("Connection", systemImage: "antenna.radiowaves.left.and.right")
                    }
            }

            DebugView(logger: coordinator.obdLogger)
                .tabItem {
                    Label("Debug", systemImage: "terminal")
                }

            SettingsView()
                .tabItem {
                    Label("Settings", systemImage: "gearshape")
                }
        }
        .onAppear {
            if connectionViewModel == nil {
                connectionViewModel = ConnectionViewModel(bluetoothManager: coordinator.bluetoothManager)
            }
            coordinator.start()
        }
    }
}
