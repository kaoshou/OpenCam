import AVFoundation
import CoreGraphics
import CoreMedia
import Darwin
import Foundation
import ScreenCaptureKit

enum HelperError: LocalizedError {
    case invalidArguments(String)
    case displayUnavailable(CGDirectDisplayID)
    case invalidAudioFormat
    case copyFailed(OSStatus)
    case conversionFailed(String)
    case fifoUnavailable(String)

    var errorDescription: String? {
        switch self {
        case .invalidArguments(let message):
            return message
        case .displayUnavailable(let displayID):
            return "Display \(displayID) is unavailable"
        case .invalidAudioFormat:
            return "ScreenCaptureKit returned an unsupported audio format"
        case .copyFailed(let status):
            return "Unable to copy PCM samples (OSStatus \(status))"
        case .conversionFailed(let message):
            return "Unable to convert PCM samples: \(message)"
        case .fifoUnavailable(let path):
            return "Unable to open audio FIFO: \(path)"
        }
    }
}

struct Options {
    let fifoPath: String
    let displayID: CGDirectDisplayID

    static func parse(_ arguments: [String]) throws -> Options {
        var fifoPath: String?
        var displayID: CGDirectDisplayID?
        var index = 0

        while index < arguments.count {
            let flag = arguments[index]
            guard index + 1 < arguments.count else {
                throw HelperError.invalidArguments("Missing value for \(flag)")
            }

            let value = arguments[index + 1]
            switch flag {
            case "--fifo":
                guard fifoPath == nil, value.hasPrefix("/") else {
                    throw HelperError.invalidArguments(
                        "--fifo must be supplied once with an absolute path")
                }
                fifoPath = value
            case "--display-id":
                guard displayID == nil, let parsed = UInt32(value) else {
                    throw HelperError.invalidArguments(
                        "--display-id must be supplied once as UInt32")
                }
                displayID = parsed
            default:
                throw HelperError.invalidArguments("Unknown argument: \(flag)")
            }
            index += 2
        }

        guard let fifoPath, let displayID else {
            throw HelperError.invalidArguments(
                "Required arguments: --fifo <absolute-path> --display-id <UInt32>")
        }

        return Options(fifoPath: fifoPath, displayID: displayID)
    }
}

extension CMSampleBuffer {
    func makePCMBuffer() throws -> AVAudioPCMBuffer {
        guard let description = CMSampleBufferGetFormatDescription(self) else {
            throw HelperError.invalidAudioFormat
        }
        let format = AVAudioFormat(cmAudioFormatDescription: description)

        let frames = AVAudioFrameCount(CMSampleBufferGetNumSamples(self))
        guard let buffer = AVAudioPCMBuffer(
            pcmFormat: format,
            frameCapacity: frames)
        else {
            throw HelperError.invalidAudioFormat
        }

        buffer.frameLength = frames
        let status = CMSampleBufferCopyPCMDataIntoAudioBufferList(
            self,
            at: 0,
            frameCount: Int32(frames),
            into: buffer.mutableAudioBufferList)
        guard status == noErr else {
            throw HelperError.copyFailed(status)
        }
        return buffer
    }
}

final class PCMWriter {
    private let fifoPath: String
    private let targetFormat = AVAudioFormat(
        commonFormat: .pcmFormatInt16,
        sampleRate: 48_000,
        channels: 2,
        interleaved: true)!
    private var sourceFormat: AVAudioFormat?
    private var converter: AVAudioConverter?
    private var fifoHandle: FileHandle?

    init(fifoPath: String) {
        self.fifoPath = fifoPath
    }

    func write(_ sampleBuffer: CMSampleBuffer) throws {
        let input = try sampleBuffer.makePCMBuffer()
        if sourceFormat == nil || !sourceFormat!.isEqual(input.format) {
            sourceFormat = input.format
            converter = AVAudioConverter(from: input.format, to: targetFormat)
        }

        guard let converter else {
            throw HelperError.conversionFailed("AVAudioConverter creation failed")
        }

        let ratio = targetFormat.sampleRate / input.format.sampleRate
        let capacity = AVAudioFrameCount(
            ceil(Double(input.frameLength) * ratio) + 32)
        guard let output = AVAudioPCMBuffer(
            pcmFormat: targetFormat,
            frameCapacity: capacity)
        else {
            throw HelperError.invalidAudioFormat
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
            throw HelperError.conversionFailed(
                conversionError?.localizedDescription ?? "unknown error")
        }

        guard output.frameLength > 0 else { return }
        if fifoHandle == nil {
            fifoHandle = FileHandle(forWritingAtPath: fifoPath)
        }
        guard let fifoHandle else {
            throw HelperError.fifoUnavailable(fifoPath)
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

final class SystemAudioCapture: NSObject, SCStreamOutput, SCStreamDelegate {
    private let options: Options
    private let writer: PCMWriter
    private let outputQueue = DispatchQueue(
        label: "com.kaoshou.opencam.system-audio")
    private var stream: SCStream?

    init(options: Options) {
        self.options = options
        self.writer = PCMWriter(fifoPath: options.fifoPath)
    }

    func start() async throws {
        let content = try await SCShareableContent.excludingDesktopWindows(
            false,
            onScreenWindowsOnly: true)
        guard let display = content.displays.first(where: {
            $0.displayID == options.displayID
        }) ?? content.displays.first else {
            throw HelperError.displayUnavailable(options.displayID)
        }

        let filter = SCContentFilter(
            display: display,
            excludingApplications: [],
            exceptingWindows: [])
        let configuration = SCStreamConfiguration()
        configuration.capturesAudio = true
        configuration.excludesCurrentProcessAudio = true
        configuration.sampleRate = 48_000
        configuration.channelCount = 2
        configuration.showsCursor = false

        let stream = SCStream(
            filter: filter,
            configuration: configuration,
            delegate: self)
        try stream.addStreamOutput(
            self,
            type: .audio,
            sampleHandlerQueue: outputQueue)
        self.stream = stream
        try await stream.startCapture()
    }

    func stop() async {
        if let stream {
            try? await stream.stopCapture()
        }
        stream = nil
        writer.close()
    }

    func stream(
        _ stream: SCStream,
        didOutputSampleBuffer sampleBuffer: CMSampleBuffer,
        of outputType: SCStreamOutputType)
    {
        guard outputType == .audio, sampleBuffer.isValid else { return }
        do {
            try writer.write(sampleBuffer)
        } catch {
            writeErrorAndExit(error, code: 4)
        }
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        writeErrorAndExit(error, code: 5)
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
    let options = try Options.parse(Array(CommandLine.arguments.dropFirst()))
    let capture = SystemAudioCapture(options: options)

    signal(SIGINT, SIG_IGN)
    signal(SIGTERM, SIG_IGN)
    let interruptSource = DispatchSource.makeSignalSource(
        signal: SIGINT,
        queue: .main)
    let terminationSource = DispatchSource.makeSignalSource(
        signal: SIGTERM,
        queue: .main)
    let stopHandler: @Sendable () -> Void = {
        _ = Task {
            await capture.stop()
            exit(0)
        }
    }
    interruptSource.setEventHandler(handler: stopHandler)
    terminationSource.setEventHandler(handler: stopHandler)
    interruptSource.resume()
    terminationSource.resume()

    Task {
        do {
            try await capture.start()
            writeStderr("READY sample-rate=48000 channels=2 format=s16le")
        } catch {
            writeErrorAndExit(error, code: 3)
        }
    }

    RunLoop.main.run()
} catch {
    writeErrorAndExit(error, code: 2)
}
