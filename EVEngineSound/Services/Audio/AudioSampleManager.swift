import AVFoundation

/// Loads and caches WAV audio samples from the app bundle
final class AudioSampleManager {

    /// Standard output format: 44100 Hz, Float32, mono
    static let standardFormat = AVAudioFormat(
        commonFormat: .pcmFormatFloat32,
        sampleRate: 44100,
        channels: 1,
        interleaved: false
    )!

    private var cache: [String: AVAudioPCMBuffer] = [:]

    // MARK: - Public API

    /// Loads all WAV samples for a given audio profile.
    /// Returns a dictionary keyed by layer ID.
    func loadSamples(for profile: AudioProfile) -> [String: AVAudioPCMBuffer] {
        var result: [String: AVAudioPCMBuffer] = [:]
        for layer in profile.layers {
            do {
                let buffer = try loadBuffer(for: layer)
                result[layer.id] = buffer
            } catch {
                print("[AudioSampleManager] Failed to load \(layer.resourcePath): \(error)")
            }
        }
        return result
    }

    /// Removes all cached buffers
    func clearCache() {
        cache.removeAll()
    }

    // MARK: - Private

    private func loadBuffer(for layer: AudioLayer) throws -> AVAudioPCMBuffer {
        if let cached = cache[layer.id] {
            return cached
        }

        guard let url = Bundle.main.url(
            forResource: layer.filename,
            withExtension: "wav",
            subdirectory: "Audio/\(layer.folder)"
        ) else {
            throw AudioSampleError.fileNotFound(layer.resourcePath)
        }

        let file = try AVAudioFile(forReading: url)
        let buffer = try readAndConvert(file: file)
        cache[layer.id] = buffer
        return buffer
    }

    private func readAndConvert(file: AVAudioFile) throws -> AVAudioPCMBuffer {
        let sourceFormat = file.processingFormat
        let frameCount = AVAudioFrameCount(file.length)

        // If source already matches our standard format, read directly
        if sourceFormat.sampleRate == Self.standardFormat.sampleRate
            && sourceFormat.commonFormat == Self.standardFormat.commonFormat
            && sourceFormat.channelCount == Self.standardFormat.channelCount {
            guard let buffer = AVAudioPCMBuffer(
                pcmFormat: sourceFormat,
                frameCapacity: frameCount
            ) else {
                throw AudioSampleError.bufferCreationFailed
            }
            try file.read(into: buffer)
            return buffer
        }

        // Read in source format first
        guard let sourceBuffer = AVAudioPCMBuffer(
            pcmFormat: sourceFormat,
            frameCapacity: frameCount
        ) else {
            throw AudioSampleError.bufferCreationFailed
        }
        try file.read(into: sourceBuffer)

        // Convert to standard format
        guard let converter = AVAudioConverter(
            from: sourceFormat,
            to: Self.standardFormat
        ) else {
            throw AudioSampleError.converterCreationFailed
        }

        let ratio = Self.standardFormat.sampleRate / sourceFormat.sampleRate
        let outputFrameCount = AVAudioFrameCount(
            Double(frameCount) * ratio
        )
        guard let outputBuffer = AVAudioPCMBuffer(
            pcmFormat: Self.standardFormat,
            frameCapacity: outputFrameCount
        ) else {
            throw AudioSampleError.bufferCreationFailed
        }

        var error: NSError?
        let status = converter.convert(to: outputBuffer, error: &error) { _, outStatus in
            outStatus.pointee = .haveData
            return sourceBuffer
        }

        if status == .error, let error = error {
            throw AudioSampleError.conversionFailed(error)
        }

        return outputBuffer
    }
}

// MARK: - Errors

enum AudioSampleError: LocalizedError {
    case fileNotFound(String)
    case bufferCreationFailed
    case converterCreationFailed
    case conversionFailed(Error)

    var errorDescription: String? {
        switch self {
        case .fileNotFound(let path):
            return "Audio file not found: \(path)"
        case .bufferCreationFailed:
            return "Failed to create audio buffer"
        case .converterCreationFailed:
            return "Failed to create audio format converter"
        case .conversionFailed(let underlying):
            return "Audio format conversion failed: \(underlying.localizedDescription)"
        }
    }
}
