import XCTest
@testable import EVEngineSound

final class RPMAudioMapperTests: XCTestCase {

    private let mapper = RPMAudioMapper()
    private let accuracy: Float = 0.01

    // MARK: - Helper: extract layer param by ID

    private func param(
        _ layerId: String,
        from params: [LayerParams]
    ) -> LayerParams? {
        params.first { $0.layerId == layerId }
    }

    // MARK: - Equal-Power Crossfade Boundary Tests (Ferrari 458)

    /// At the low anchor RPM, lowGain should be ~1.0 and highGain should be ~0.0
    func testCrossfadeAtLowAnchor_Ferrari458() {
        // Ferrari 458: onLow anchors at 5300, onHigh at 7900
        let params = mapper.calculateLayerParams(
            rpm: 5300,
            pedalPosition: 1.0,
            profile: .ferrari458
        )

        // on_low layer at full pedal with RPM at low anchor
        let onLow = param("458_power", from: params)
        XCTAssertNotNil(onLow)
        // At low anchor, lowGain = cos(0) = 1.0, highGain = sin(0) = 0.0
        // on_low gain = onGain(1.0) * lowGain(1.0) = 1.0
        XCTAssertEqual(onLow!.gain, 1.0, accuracy: accuracy)

        let onHigh = param("458_on_high", from: params)
        XCTAssertNotNil(onHigh)
        // on_high gain = onGain(1.0) * highGain(0.0) = 0.0
        XCTAssertEqual(onHigh!.gain, 0.0, accuracy: accuracy)
    }

    /// At the high anchor RPM, highGain should be ~1.0 and lowGain should be ~0.0
    func testCrossfadeAtHighAnchor_Ferrari458() {
        let params = mapper.calculateLayerParams(
            rpm: 7900,
            pedalPosition: 1.0,
            profile: .ferrari458
        )

        let onLow = param("458_power", from: params)
        XCTAssertNotNil(onLow)
        XCTAssertEqual(onLow!.gain, 0.0, accuracy: accuracy)

        let onHigh = param("458_on_high", from: params)
        XCTAssertNotNil(onHigh)
        XCTAssertEqual(onHigh!.gain, 1.0, accuracy: accuracy)
    }

    /// At the midpoint RPM, both gains should be approximately 0.707 (sqrt(2)/2)
    func testCrossfadeAtMidpoint_Ferrari458() {
        // Midpoint between 5300 and 7900 is 6600
        let midRPM = (5300.0 + 7900.0) / 2.0
        let params = mapper.calculateLayerParams(
            rpm: midRPM,
            pedalPosition: 1.0,
            profile: .ferrari458
        )

        let onLow = param("458_power", from: params)
        XCTAssertNotNil(onLow)
        XCTAssertEqual(onLow!.gain, 0.707, accuracy: 0.02)

        let onHigh = param("458_on_high", from: params)
        XCTAssertNotNil(onHigh)
        XCTAssertEqual(onHigh!.gain, 0.707, accuracy: 0.02)
    }

    // MARK: - Playback Rate Tests

    func testPlaybackRate_exactAnchor() {
        // At anchor RPM, rate should be 1.0
        let rate = mapper.playbackRate(rpm: 5300, anchorRPM: 5300)
        XCTAssertEqual(rate, 1.0, accuracy: accuracy)
    }

    func testPlaybackRate_doubleAnchor() {
        let rate = mapper.playbackRate(rpm: 10600, anchorRPM: 5300)
        XCTAssertEqual(rate, 2.0, accuracy: accuracy)
    }

    func testPlaybackRate_halfAnchor() {
        let rate = mapper.playbackRate(rpm: 2650, anchorRPM: 5300)
        XCTAssertEqual(rate, 0.5, accuracy: accuracy)
    }

    func testPlaybackRate_clampedLow() {
        // Very low RPM should clamp to 0.1
        let rate = mapper.playbackRate(rpm: 100, anchorRPM: 7900)
        XCTAssertEqual(rate, 0.1, accuracy: accuracy)
    }

    func testPlaybackRate_clampedHigh() {
        // Very high RPM relative to anchor should clamp to 4.0
        let rate = mapper.playbackRate(rpm: 50000, anchorRPM: 5300)
        XCTAssertEqual(rate, 4.0, accuracy: accuracy)
    }

    // MARK: - Limiter Ratio Tests

    func testLimiterRatio_belowSoftStart_Ferrari458() {
        // softLimiterRPM = 8200, softLimiterStart = 8200 * 0.93 = 7626
        let ratio = mapper.limiterRatio(rpm: 7000, profile: .ferrari458)
        XCTAssertEqual(ratio, 0.0, accuracy: accuracy)
    }

    func testLimiterRatio_atLimiterRPM_Ferrari458() {
        let ratio = mapper.limiterRatio(rpm: 9000, profile: .ferrari458)
        XCTAssertEqual(ratio, 1.0, accuracy: accuracy)
    }

    func testLimiterRatio_midway_Ferrari458() {
        // softStart = 7626, limiter = 9000, range = 1374
        // At midpoint 8313: (8313 - 7626) / 1374 = 0.5
        let softStart = 8200.0 * 0.93
        let mid = (softStart + 9000.0) / 2.0
        let ratio = mapper.limiterRatio(rpm: mid, profile: .ferrari458)
        XCTAssertEqual(ratio, 0.5, accuracy: 0.02)
    }

    func testLimiterRatio_aboveLimiter_clampedToOne() {
        let ratio = mapper.limiterRatio(rpm: 12000, profile: .ferrari458)
        XCTAssertEqual(ratio, 1.0, accuracy: accuracy)
    }

    // MARK: - Pedal Position Modulation

    func testPedalZero_usesMinimumIdle() {
        let params = mapper.calculateLayerParams(
            rpm: 5300,
            pedalPosition: 0.0,
            profile: .ferrari458
        )

        // onGain = max(0.0, 0.05) = 0.05
        // offGain = 0.95
        // At lowAnchor: lowGain = 1.0, so on_low = 0.05, off_low = 0.95
        let onLow = param("458_power", from: params)
        XCTAssertNotNil(onLow)
        XCTAssertEqual(onLow!.gain, 0.05, accuracy: accuracy)

        let offLow = param("458_off_midhigh", from: params)
        XCTAssertNotNil(offLow)
        XCTAssertEqual(offLow!.gain, 0.95, accuracy: accuracy)
    }

    func testPedalHalf_splitGains() {
        let params = mapper.calculateLayerParams(
            rpm: 5300,
            pedalPosition: 0.5,
            profile: .ferrari458
        )

        // onGain = 0.5, offGain = 0.5
        // At lowAnchor: lowGain = 1.0
        // on_low = 0.5, off_low = 0.5
        let onLow = param("458_power", from: params)
        XCTAssertNotNil(onLow)
        XCTAssertEqual(onLow!.gain, 0.5, accuracy: accuracy)

        let offLow = param("458_off_midhigh", from: params)
        XCTAssertNotNil(offLow)
        XCTAssertEqual(offLow!.gain, 0.5, accuracy: accuracy)
    }

    // MARK: - Procar Profile Tests

    func testCrossfadeAtLowAnchor_Procar() {
        // Procar low band layers (onLow + offLow):
        //   procar_on_midhigh (5900), procar_on_low (3200), procar_off_lower (3200)
        //   average = (5900 + 3200 + 3200) / 3 = 4100
        // Procar high band layers (onHigh + offHigh):
        //   procar_on_high (8430), procar_off_midhigh (5900)
        //   average = (8430 + 5900) / 2 = 7165
        let lowAnchor = (5900.0 + 3200.0 + 3200.0) / 3.0  // 4100
        let params = mapper.calculateLayerParams(
            rpm: lowAnchor,
            pedalPosition: 1.0,
            profile: .procar
        )

        let onLow = param("procar_on_midhigh", from: params)
        XCTAssertNotNil(onLow)
        // At lowAnchor, lowGain = 1.0, so on_low = 1.0
        XCTAssertEqual(onLow!.gain, 1.0, accuracy: accuracy)

        let onHigh = param("procar_on_high", from: params)
        XCTAssertNotNil(onHigh)
        XCTAssertEqual(onHigh!.gain, 0.0, accuracy: accuracy)
    }

    func testPlaybackRates_Procar() {
        let params = mapper.calculateLayerParams(
            rpm: 5900,
            pedalPosition: 1.0,
            profile: .procar
        )

        // procar_on_midhigh has anchor 5900, so rate should be 1.0
        let midHigh = param("procar_on_midhigh", from: params)
        XCTAssertNotNil(midHigh)
        XCTAssertEqual(midHigh!.playbackRate, 1.0, accuracy: accuracy)

        // procar_on_low has anchor 3200, so rate should be 5900/3200 = 1.84375
        let low = param("procar_on_low", from: params)
        XCTAssertNotNil(low)
        XCTAssertEqual(low!.playbackRate, Float(5900.0 / 3200.0), accuracy: accuracy)
    }

    // MARK: - BAC Mono Profile Tests

    func testCrossfadeAtHighAnchor_BACMono() {
        // BAC Mono high band layers (onHigh + offHigh):
        //   bac_on_high (5000), bac_off_veryhigh (7000), bac_off_high (5000)
        //   average = (5000 + 7000 + 5000) / 3 = 5666.67
        let highAnchor = (5000.0 + 7000.0 + 5000.0) / 3.0
        let params = mapper.calculateLayerParams(
            rpm: highAnchor,
            pedalPosition: 1.0,
            profile: .bacMono
        )

        let onHigh = param("bac_on_high", from: params)
        XCTAssertNotNil(onHigh)
        XCTAssertEqual(onHigh!.gain, 1.0, accuracy: accuracy)
    }

    func testLimiterRatio_BACMono() {
        // softLimiterRPM = 7500, softStart = 7500*0.93 = 6975
        // limiterRPM = 8500
        // At 8500 -> ratio = 1.0
        let ratio = mapper.limiterRatio(rpm: 8500, profile: .bacMono)
        XCTAssertEqual(ratio, 1.0, accuracy: accuracy)

        // Below soft start -> 0.0
        let ratioLow = mapper.limiterRatio(rpm: 6000, profile: .bacMono)
        XCTAssertEqual(ratioLow, 0.0, accuracy: accuracy)
    }

    func testBACMono_limiterLayerGain() {
        let params = mapper.calculateLayerParams(
            rpm: 8500,
            pedalPosition: 1.0,
            profile: .bacMono
        )

        let limiter = param("bac_limiter", from: params)
        XCTAssertNotNil(limiter)
        XCTAssertEqual(limiter!.gain, 1.0, accuracy: accuracy)
    }

    // MARK: - Edge Cases

    func testRPMBelowMinimum() {
        // RPM below low anchor should clamp crossfade to (1.0, 0.0)
        let params = mapper.calculateLayerParams(
            rpm: 0,
            pedalPosition: 1.0,
            profile: .ferrari458
        )

        let onLow = param("458_power", from: params)
        XCTAssertNotNil(onLow)
        XCTAssertEqual(onLow!.gain, 1.0, accuracy: accuracy)
    }

    func testRPMAboveMaximum() {
        // RPM way above high anchor should clamp crossfade to (0.0, 1.0)
        let params = mapper.calculateLayerParams(
            rpm: 20000,
            pedalPosition: 1.0,
            profile: .ferrari458
        )

        let onHigh = param("458_on_high", from: params)
        XCTAssertNotNil(onHigh)
        XCTAssertEqual(onHigh!.gain, 1.0, accuracy: accuracy)
    }

    func testAllLayersHaveParams_Ferrari458() {
        let params = mapper.calculateLayerParams(
            rpm: 5000,
            pedalPosition: 0.5,
            profile: .ferrari458
        )
        // Ferrari 458 has 7 layers
        XCTAssertEqual(params.count, AudioProfile.ferrari458.layers.count)
    }

    func testAllLayersHaveParams_Procar() {
        let params = mapper.calculateLayerParams(
            rpm: 5000,
            pedalPosition: 0.5,
            profile: .procar
        )
        XCTAssertEqual(params.count, AudioProfile.procar.layers.count)
    }

    func testAllLayersHaveParams_BACMono() {
        let params = mapper.calculateLayerParams(
            rpm: 5000,
            pedalPosition: 0.5,
            profile: .bacMono
        )
        XCTAssertEqual(params.count, AudioProfile.bacMono.layers.count)
    }

    func testPlaybackRateWithZeroAnchor() {
        // Edge case: zero anchor should return 1.0 (guard clause)
        let rate = mapper.playbackRate(rpm: 5000, anchorRPM: 0)
        XCTAssertEqual(rate, 1.0, accuracy: accuracy)
    }
}
