import SwiftUI

struct DemoView: View {
    @StateObject private var viewModel = DemoViewModel()

    var body: some View {
        NavigationView {
            ScrollView {
                VStack(spacing: 24) {
                    // RPM Gauge
                    RPMGaugeView(rpm: viewModel.rpm, maxRPM: viewModel.selectedProfile.maxRPM)
                        .frame(height: 280)
                        .padding(.horizontal)

                    // Pedal gauge
                    PedalGaugeView(pedalPosition: viewModel.pedalPosition)

                    // Profile picker
                    profilePicker

                    // RPM Slider
                    sliderSection(
                        title: "RPM",
                        value: $viewModel.rpm,
                        range: 0...viewModel.selectedProfile.maxRPM,
                        step: 1,
                        displayValue: "\(Int(viewModel.rpm))"
                    )
                    .disabled(viewModel.isAutoRevving)

                    // Pedal slider
                    sliderSection(
                        title: "Pedal Position",
                        value: $viewModel.pedalPosition,
                        range: 0...100,
                        step: 1,
                        displayValue: "\(Int(viewModel.pedalPosition))%"
                    )
                    .disabled(viewModel.isAutoRevving)

                    // Auto Rev button
                    autoRevButton
                }
                .padding(.vertical)
            }
            .background(Color(.systemGroupedBackground))
            .navigationTitle("Demo")
            .onAppear {
                viewModel.start()
            }
            .onDisappear {
                viewModel.stop()
            }
        }
    }

    // MARK: - Subviews

    private var profilePicker: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Profile")
                .font(.subheadline)
                .fontWeight(.semibold)
                .foregroundColor(.secondary)
                .padding(.horizontal)

            Picker("Profile", selection: $viewModel.selectedProfile) {
                ForEach(AudioProfile.allProfiles) { profile in
                    Text(profile.name).tag(profile)
                }
            }
            .pickerStyle(.segmented)
            .padding(.horizontal)
        }
    }

    private func sliderSection(title: String, value: Binding<Double>,
                                range: ClosedRange<Double>, step: Double,
                                displayValue: String) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(title)
                    .font(.subheadline)
                    .fontWeight(.semibold)
                    .foregroundColor(.secondary)
                Spacer()
                Text(displayValue)
                    .font(.subheadline)
                    .fontWeight(.bold)
                    .monospacedDigit()
            }
            .padding(.horizontal)

            Slider(value: value, in: range, step: step)
                .tint(.accentColor)
                .padding(.horizontal)
        }
    }

    private var autoRevButton: some View {
        Button(action: { viewModel.toggleAutoRev() }) {
            HStack(spacing: 8) {
                Image(systemName: viewModel.isAutoRevving ? "stop.fill" : "play.fill")
                Text(viewModel.isAutoRevving ? "Stop Auto Rev" : "Auto Rev")
                    .fontWeight(.semibold)
            }
            .frame(maxWidth: .infinity)
            .padding()
            .background(viewModel.isAutoRevving ? Color.gaugeRed : Color.accentColor)
            .foregroundColor(.white)
            .cornerRadius(14)
        }
        .padding(.horizontal)
        .padding(.bottom)
    }
}

#Preview {
    DemoView()
}
