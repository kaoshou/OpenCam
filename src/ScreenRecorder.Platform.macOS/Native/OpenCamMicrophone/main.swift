// SPDX-License-Identifier: AGPL-3.0-or-later
import AVFoundation
import Darwin
import Foundation

enum MicrophoneHelperError: LocalizedError {
    case invalidArguments(String)
    case permissionDenied
    case invalidInputFormat
    case conversionFailed(String)
    case fifoUnavailable(String)

    var errorDescription: String? {
        switch self {
        case .invalidArguments(let message):
            return message
        case .permissionDenied:
            return "Microphone access was denied"
        case .invalidInputFormat:
            return "The selected microphone returned an unsupported audio format"
        case .conversionFailed(let message):
            return "Unable to convert microphone samples: \(message)"
        case .fifoUnavailable(let path):
            return "Unable to open microphone FIFO: \(path)"
        }
    }
}

struct MicrophoneOptions {
    let fifoPath: String

    static func parse(_ arguments: [String]) throws -> MicrophoneOptions {
        guard arguments.count == 2,
              arguments[0] == "--fifo",
              arguments[1].hasPrefix("/") else {
            throw MicrophoneHelperError.invalidArguments(
                "Required arguments: --fifo <absolute-path>")
        }

        return MicrophoneOptions(fifoPath: arguments[1])
    }
}

final class MicrophonePCMWriter {
    private let fifoPath: String
    private let targetFormat = AVAudioFormat(
        commonFormat: .pcmFormatInt16,
        sampleRate: 48_000,
        channels: 1,
        interleaved: true)!
    private var sourceFormat: AVAudioFormat?
    private var converter: AVAudioConverter?
    private var fifoHandle: FileHandle?

    init(fifoPath: String) {
        self.fifoPath = fifoPath
    }

    func write(_ input: AVAudioPCMBuffer) throws {
        if sourceFormat == nil || !sourceFormat!.isEqual(input.format) {
            sourceFormat = input.format
            converter = AVAudioConverter(
                from: input.format,
                to: targetFormat)
        }

        guard let converter else {
            throw MicrophoneHelperError.conversionFailed(
                "AVAudioConverter creation failed")
        }

        let ratio = targetFormat.sampleRate / input.format.sampleRate
        let capacity = AVAudioFrameCount(
            ceil(Double(input.frameLength) * ratio) + 32)
        guard let output = AVAudioPCMBuffer(
            pcmFormat: targetFormat,
            frameCapacity: capacity) else {
            throw MicrophoneHelperError.invalidInputFormat
        }

        var suppliedInput = false
        var conversionError: NSError?
        let status = converter.convert(to: output, error: &conversionError) {
            _, outputStatus in
            if suppliedInput {
                outputStatus.pointee = .noDataNow
                return nil
            }
            suppliedInput = true
            outputStatus.pointee = .haveData
            return input
        }

        guard status != .error, conversionError == nil else {
            throw MicrophoneHelperError.conversionFailed(
                conversionError?.localizedDescription ?? "unknown error")
        }

        guard output.frameLength > 0 else { return }
        if fifoHandle == nil {
            fifoHandle = FileHandle(forWritingAtPath: fifoPath)
        }
        guard let fifoHandle else {
            throw MicrophoneHelperError.fifoUnavailable(fifoPath)
        }

        let audioBuffer = output.audioBufferList.pointee.mBuffers
        guard let bytes = audioBuffer.mData else { return }
        try fifoHandle.write(contentsOf: Data(
            bytes: bytes,
            count: Int(audioBuffer.mDataByteSize)))
    }

    func close() {
        try? fifoHandle?.close()
        fifoHandle = nil
    }
}

final class MicrophoneCapture {
    private let engine = AVAudioEngine()
    private let writer: MicrophonePCMWriter
    private var tapInstalled = false

    init(options: MicrophoneOptions) {
        writer = MicrophonePCMWriter(fifoPath: options.fifoPath)
    }

    func start() async throws {
        guard await requestMicrophonePermission() else {
            throw MicrophoneHelperError.permissionDenied
        }

        let inputNode = engine.inputNode
        let inputFormat = inputNode.outputFormat(forBus: 0)
        guard inputFormat.sampleRate > 0,
              inputFormat.channelCount > 0 else {
            throw MicrophoneHelperError.invalidInputFormat
        }

        inputNode.installTap(
            onBus: 0,
            bufferSize: 1_024,
            format: nil) { [writer] buffer, _ in
                do {
                    try writer.write(buffer)
                } catch {
                    writeErrorAndExit(error, code: 4)
                }
            }
        tapInstalled = true

        engine.prepare()
        try engine.start()
    }

    func stop() {
        if tapInstalled {
            engine.inputNode.removeTap(onBus: 0)
            tapInstalled = false
        }
        engine.stop()
        writer.close()
    }

    private func requestMicrophonePermission() async -> Bool {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized:
            return true
        case .notDetermined:
            return await withCheckedContinuation { continuation in
                AVCaptureDevice.requestAccess(for: .audio) { granted in
                    continuation.resume(returning: granted)
                }
            }
        default:
            return false
        }
    }
}

func writeStderr(_ line: String) {
    FileHandle.standardError.write(Data((line + "\n").utf8))
}

func writeErrorAndExit(_ error: Error, code: Int32) -> Never {
    writeStderr("ERROR \(error.localizedDescription)")
    exit(code)
}

do {
    let options = try MicrophoneOptions.parse(
        Array(CommandLine.arguments.dropFirst()))
    let capture = MicrophoneCapture(options: options)

    signal(SIGINT, SIG_IGN)
    signal(SIGTERM, SIG_IGN)
    let interruptSource = DispatchSource.makeSignalSource(
        signal: SIGINT,
        queue: .main)
    let terminationSource = DispatchSource.makeSignalSource(
        signal: SIGTERM,
        queue: .main)
    let stopHandler: @Sendable () -> Void = {
        capture.stop()
        exit(0)
    }
    interruptSource.setEventHandler(handler: stopHandler)
    terminationSource.setEventHandler(handler: stopHandler)
    interruptSource.resume()
    terminationSource.resume()

    Task {
        do {
            try await capture.start()
            writeStderr("READY sample-rate=48000 channels=1 format=s16le")
        } catch {
            writeErrorAndExit(error, code: 3)
        }
    }

    RunLoop.main.run()
} catch {
    writeErrorAndExit(error, code: 2)
}
