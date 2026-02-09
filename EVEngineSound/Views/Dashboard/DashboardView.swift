import SwiftUI

struct DashboardView: View {
    @EnvironmentObject private var coordinator: AppCoordinator
    @StateObject private var viewModel = DashboardViewModel()

    var body: some View {
        NavigationView {
            ScrollView {
                VStack(spacing: 24) {
                    // Connection status
                    connectionBanner

                    // RPM Gauge
                    RPMGaugeView(rpm: viewModel.rpm, maxRPM: viewModel.selectedProfile.maxRPM)
                        .frame(height: 280)
                        .padding(.horizontal)

                    // Pedal gauge
                    PedalGaugeView(pedalPosition: viewModel.pedalPosition)

                    // Current profile
                    profileCard
                }
                .padding(.vertical)
            }
            .background(Color(.systemGroupedBackground))
            .navigationTitle("Dashboard")
            .onAppear {
                viewModel.bind(to: coordinator)
            }
        }
    }

    // MARK: - Subviews

    private var connectionBanner: some View {
        HStack(spacing: 8) {
            Circle()
                .fill(viewModel.isConnected ? Color.gaugeGreen : Color(.systemGray4))
                .frame(width: 10, height: 10)
            Text(viewModel.isConnected ? "Connected" : "Not Connected")
                .font(.subheadline)
                .fontWeight(.medium)
                .foregroundColor(viewModel.isConnected ? .primary : .secondary)
            Spacer()
        }
        .padding(.horizontal)
        .padding(.vertical, 8)
        .background(
            RoundedRectangle(cornerRadius: 12)
                .fill(viewModel.isConnected
                      ? Color.gaugeGreen.opacity(0.1)
                      : Color(.systemGray6))
        )
        .padding(.horizontal)
    }

    private var profileCard: some View {
        HStack {
            VStack(alignment: .leading, spacing: 4) {
                Text("Active Profile")
                    .font(.caption)
                    .foregroundColor(.secondary)
                Text(viewModel.selectedProfile.name)
                    .font(.title3)
                    .fontWeight(.semibold)
            }
            Spacer()
            Image(systemName: "waveform")
                .font(.title2)
                .foregroundColor(.accentColor)
        }
        .padding()
        .background(
            RoundedRectangle(cornerRadius: 16)
                .fill(Color(.secondarySystemGroupedBackground))
        )
        .padding(.horizontal)
    }
}

#Preview {
    DashboardView()
        .environmentObject(AppCoordinator())
}
