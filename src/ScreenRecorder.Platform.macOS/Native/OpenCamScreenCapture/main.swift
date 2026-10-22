// SPDX-License-Identifier: AGPL-3.0-or-later
import Foundation
import ScreenCaptureKit
import CoreMedia
import CoreVideo
import VideoToolbox
import CoreGraphics
import Darwin

func diagnostic(_ text: String) { FileHandle.standardError.write(Data((text + "\n").utf8)) }
func fail(_ text: String) -> Never { diagnostic("ERROR " + text); exit(1) }
func checked(_ status: OSStatus, _ operation: String) throws {
    if status != noErr { throw NSError(domain: operation, code: Int(status)) }
}

// A streaming Matroska subset: one AVC track, unknown segment size, complete
// per-frame clusters. Every block has a wall-clock PTS, including gaps under load.
// No seeking, final index or whole-recording buffer is needed.
struct Matroska {
    static func number(_ n: UInt64) -> Data {
        var value = n.bigEndian
        return withUnsafeBytes(of: &value) { Data($0) }.drop { $0 == 0 }.nonempty
    }
    static func size(_ n: Int) -> Data {
        for count in 1...8 where UInt64(n) < (UInt64(1) << (7 * count)) - 1 {
            var value = UInt64(n) | (UInt64(1) << (7 * count))
            value = value.bigEndian
            return withUnsafeBytes(of: &value) { Data($0.suffix(count)) }
        }
        fatalError("Invalid EBML element size")
    }
    static func element(_ id: UInt64, _ data: Data) -> Data { number(id) + size(data.count) + data }
    static func uint(_ id: UInt64, _ value: UInt64) -> Data { element(id, number(value)) }
    static func text(_ id: UInt64, _ value: String) -> Data { element(id, Data(value.utf8)) }
    static func header(width: Int, height: Int, fps: Int, avcc: Data) -> Data {
        let ebml = uint(0x4286, 1) + uint(0x42F7, 1) + uint(0x42F2, 4) + uint(0x42F3, 8)
            + text(0x4282, "matroska") + uint(0x4287, 4) + uint(0x4285, 2)
        let info = uint(0x2AD7B1, 1_000_000) + text(0x4D80, "OpenCam") + text(0x5741, "OpenCam")
        let video = uint(0xB0, UInt64(width)) + uint(0xBA, UInt64(height))
        let track = uint(0xD7, 1) + uint(0x73C5, 1) + uint(0x83, 1) + uint(0x9C, 0)
            + text(0x86, "V_MPEG4/ISO/AVC") + element(0x63A2, avcc)
            + uint(0x23E383, UInt64(1_000_000_000 / fps)) + element(0xE0, video)
        return element(0x1A45DFA3, ebml) + number(0x18538067)
            + Data([0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff])
            + element(0x1549A966, info) + element(0x1654AE6B, element(0xAE, track))
    }
    static func frame(_ bytes: Data, pts: UInt64, key: Bool) -> Data {
        let block = Data([0x81, 0, 0, key ? 0x80 : 0]) + bytes
        return element(0x1F43B675, uint(0xE7, pts) + element(0xA3, block))
    }
}
extension Data { var nonempty: Data { isEmpty ? Data([0]) : self } }

final class Encoder {
    let width: Int, height: Int, fps: Int
    var session: VTCompressionSession?
    private let lock = NSLock()
    private let outputLock = NSLock()
    private var pending = 0
    private var written = 0
    private var dropped = 0
    private var lastPts: UInt64?
    private var headerWritten = false
    init(width: Int, height: Int, fps: Int, bitrate: Int) throws {
        self.width = width; self.height = height; self.fps = fps
        let specification = [kVTVideoEncoderSpecification_RequireHardwareAcceleratedVideoEncoder: true] as CFDictionary
        try checked(VTCompressionSessionCreate(allocator: nil, width: Int32(width), height: Int32(height),
            codecType: kCMVideoCodecType_H264, encoderSpecification: specification,
            imageBufferAttributes: nil, compressedDataAllocator: nil,
            outputCallback: { context, _, status, flags, sample in
                guard let context else { return }
                let encoder = Unmanaged<Encoder>.fromOpaque(context).takeUnretainedValue()
                defer { encoder.lock.lock(); encoder.pending -= 1; encoder.lock.unlock() }
                if status != noErr { fail("VideoToolbox encode status \(status)") }
                if flags.contains(.frameDropped) { encoder.lock.lock(); encoder.dropped += 1; encoder.lock.unlock(); return }
                guard let sample else { fail("VideoToolbox returned no sample") }
                do { try encoder.write(sample) } catch { fail("Encoded output: \(error)") }
            }, refcon: Unmanaged.passUnretained(self).toOpaque(), compressionSessionOut: &session), "VTCreate")
        guard let session else { throw NSError(domain: "VTCreate", code: -1) }
        for (key, value) in [
            (kVTCompressionPropertyKey_RealTime, kCFBooleanTrue as CFTypeRef),
            (kVTCompressionPropertyKey_AllowFrameReordering, kCFBooleanFalse as CFTypeRef),
            (kVTCompressionPropertyKey_ProfileLevel, kVTProfileLevel_H264_Main_AutoLevel as CFTypeRef),
            (kVTCompressionPropertyKey_ExpectedFrameRate, NSNumber(value: fps)),
            (kVTCompressionPropertyKey_AverageBitRate, NSNumber(value: bitrate * 1000)),
            (kVTCompressionPropertyKey_MaxKeyFrameInterval, NSNumber(value: fps * 2))
        ] { try checked(VTSessionSetProperty(session, key: key, value: value), "VTProperty") }
        try checked(VTCompressionSessionPrepareToEncodeFrames(session), "VTPrepare")
    }
    func encode(_ pixel: CVPixelBuffer, time: CMTime) throws {
        lock.lock()
        if pending >= 3 { dropped += 1; lock.unlock(); return }
        pending += 1; lock.unlock()
        let status = VTCompressionSessionEncodeFrame(session!, imageBuffer: pixel,
            presentationTimeStamp: time, duration: CMTime(value: 1, timescale: Int32(fps)),
            frameProperties: nil, sourceFrameRefcon: nil, infoFlagsOut: nil)
        if status != noErr { lock.lock(); pending -= 1; lock.unlock(); try checked(status, "VTEncode") }
    }
    private func write(_ sample: CMSampleBuffer) throws {
        outputLock.lock(); defer { outputLock.unlock() }
        guard let format = CMSampleBufferGetFormatDescription(sample),
            let block = CMSampleBufferGetDataBuffer(sample) else { throw NSError(domain: "Sample", code: -1) }
        let seconds = CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(sample))
        guard seconds.isFinite, seconds >= 0 else { throw NSError(domain: "PTS", code: -1) }
        let pts = UInt64((seconds * 1000).rounded())
        if let lastPts, pts <= lastPts { throw NSError(domain: "Non-monotonic PTS", code: -1) }
        if !headerWritten {
            guard let atoms = CMFormatDescriptionGetExtension(format,
                extensionKey: kCMFormatDescriptionExtension_SampleDescriptionExtensionAtoms) as? [String: Any],
                let avcc = atoms["avcC"] as? Data else { throw NSError(domain: "avcC", code: -1) }
            try FileHandle.standardOutput.write(contentsOf: Matroska.header(width: width, height: height, fps: fps, avcc: avcc))
            headerWritten = true
        }
        var bytes = Data(count: CMBlockBufferGetDataLength(block))
        try bytes.withUnsafeMutableBytes { storage in
            try checked(CMBlockBufferCopyDataBytes(block, atOffset: 0, dataLength: storage.count,
                destination: storage.baseAddress!), "SampleCopy")
        }
        let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: false) as? [[CFString: Any]]
        let key = attachments?.first?[kCMSampleAttachmentKey_NotSync] as? Bool != true
        try FileHandle.standardOutput.write(contentsOf: Matroska.frame(bytes, pts: pts, key: key))
        lastPts = pts; written += 1
    }
    func finish() throws {
        if let session {
            try checked(VTCompressionSessionCompleteFrames(session, untilPresentationTimeStamp: .invalid), "VTComplete")
            VTCompressionSessionInvalidate(session); self.session = nil
        }
        diagnostic("STOP frames=\(written) dropped=\(dropped)")
    }
    deinit { if let session { VTCompressionSessionInvalidate(session) } }
}

final class Capture: NSObject, SCStreamOutput, SCStreamDelegate {
    let encoder: Encoder
    let queue = DispatchQueue(label: "OpenCam.capture")
    let encodeQueue = DispatchQueue(label: "OpenCam.encode")
    let lock = NSLock()
    var latest: CVPixelBuffer?
    var capturedFrames: UInt64 = 0
    var sampledSequence: UInt64 = 0
    var repeatedSamples: UInt64 = 0
    var stream: SCStream?
    var timer: DispatchSourceTimer?
    var origin: CMTime?
    let displayID: CGDirectDisplayID
    let displayWidth: Int, displayHeight: Int
    let initialBounds: CGRect
    let initialRotation: Double
    init(encoder: Encoder, display: CGDirectDisplayID, displayWidth: Int, displayHeight: Int) {
        self.encoder = encoder; self.displayID = display
        self.displayWidth = displayWidth; self.displayHeight = displayHeight
        self.initialBounds = CGDisplayBounds(display)
        self.initialRotation = CGDisplayRotation(display)
    }
    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen, sampleBuffer.isValid,
            let info = (CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]])?.first,
            let status = info[.status] as? Int, status == SCFrameStatus.complete.rawValue,
            let image = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        lock.lock(); latest = image; capturedFrames += 1; lock.unlock()
    }
    func stream(_ stream: SCStream, didStopWithError error: Error) { fail("ScreenCaptureKit stopped: \(error)") }
    func start() async throws {
        try await stream!.startCapture()
        // A fixed sampling clock reuses the latest surface for static desktops.
        // Late ticks keep elapsed timestamps; they never burst to catch up.
        let timer = DispatchSource.makeTimerSource(queue: encodeQueue)
        timer.schedule(deadline: .now(), repeating: .nanoseconds(1_000_000_000 / encoder.fps), leeway: .milliseconds(1))
        timer.setEventHandler { [weak self] in
            guard let self else { return }
            guard CGDisplayIsActive(displayID) != 0, CGDisplayPixelsWide(displayID) == displayWidth,
                CGDisplayPixelsHigh(displayID) == displayHeight, CGDisplayBounds(displayID) == initialBounds,
                CGDisplayRotation(displayID) == initialRotation else { fail("Capture display changed") }
            lock.lock(); let image = latest; let sequence = capturedFrames; lock.unlock()
            guard let image else { return }
            if sequence == sampledSequence { repeatedSamples += 1 }
            sampledSequence = sequence
            let now = CMClockGetTime(CMClockGetHostTimeClock())
            if origin == nil { origin = now }
            do { try encoder.encode(image, time: CMTimeSubtract(now, origin!)) }
            catch { fail("Encode: \(error)") }
        }
        self.timer = timer; timer.resume()
    }
    func stop() async throws {
        timer?.cancel()
        encodeQueue.sync {} // no encode submission remains after this barrier
        try await stream?.stopCapture()
        queue.sync {}
        try encoder.finish()
        diagnostic("CAPTURE delivered=\(capturedFrames) repeatedSamples=\(repeatedSamples)")
        lock.withLock { latest = nil }
    }
}

@main struct Main {
    static func main() async {
        signal(SIGPIPE, SIG_IGN)
        do {
            let args = Array(CommandLine.arguments.dropFirst())
            if args == ["--synthetic-test"] {
                let encoder = try Encoder(width: 320, height: 180, fps: 30, bitrate: 1000)
                for index in 0..<60 {
                    var pixel: CVPixelBuffer?
                    try checked(CVPixelBufferCreate(nil, 320, 180, kCVPixelFormatType_32BGRA,
                        [kCVPixelBufferIOSurfacePropertiesKey: [:]] as CFDictionary, &pixel), "PixelBuffer")
                    CVPixelBufferLockBaseAddress(pixel!, [])
                    memset(CVPixelBufferGetBaseAddress(pixel!), Int32(index * 3), CVPixelBufferGetDataSize(pixel!))
                    CVPixelBufferUnlockBaseAddress(pixel!, [])
                    // A deliberate gap verifies that elapsed timestamps survive muxing.
                    try encoder.encode(pixel!, time: CMTime(value: Int64(index + (index >= 30 ? 15 : 0)), timescale: 30))
                    try checked(VTCompressionSessionCompleteFrames(encoder.session!, untilPresentationTimeStamp: .invalid), "TestDrain")
                }
                try encoder.finish(); return
            }
            guard args.count == 10, args.allSatisfy({ Int($0) != nil }) else { fail("Invalid arguments") }
            let v = args.map { Int($0)! }
            guard v[0] > 0, v[0] <= Int(UInt32.max), v[1] >= 0, v[2] >= 0,
                v[3] >= 2, v[4] >= 2, v[3] <= 16384, v[4] <= 16384,
                v[3] % 2 == 0, v[4] % 2 == 0, v[5] >= v[3], v[6] >= v[4],
                v[1] <= v[5] - v[3], v[2] <= v[6] - v[4],
                (1...120).contains(v[7]), (100...200000).contains(v[8]), (0...1).contains(v[9]) else { fail("Invalid capture geometry or rate") }
            let id = CGDirectDisplayID(v[0])
            guard CGDisplayPixelsWide(id) == v[5], CGDisplayPixelsHigh(id) == v[6] else { fail("Display dimensions changed") }
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
            guard let display = content.displays.first(where: { $0.displayID == id }) else { fail("Display unavailable") }
            let config = SCStreamConfiguration()
            config.width = v[3]; config.height = v[4]
            let displayBounds = CGDisplayBounds(id)
            let sx = displayBounds.width / Double(v[5]), sy = displayBounds.height / Double(v[6])
            config.sourceRect = CGRect(x: Double(v[1]) * sx, y: Double(v[2]) * sy,
                width: Double(v[3]) * sx, height: Double(v[4]) * sy)
            config.minimumFrameInterval = CMTime(value: 1, timescale: Int32(v[7]))
            config.queueDepth = 3; config.showsCursor = v[9] == 1
            config.pixelFormat = kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange
            config.colorSpaceName = CGColorSpace.sRGB
            config.capturesAudio = false
            let encoder = try Encoder(width: v[3], height: v[4], fps: v[7], bitrate: v[8])
            let capture = Capture(encoder: encoder, display: id, displayWidth: v[5], displayHeight: v[6])
            let filter = SCContentFilter(display: display, excludingWindows: [])
            capture.stream = SCStream(filter: filter, configuration: config, delegate: capture)
            try capture.stream!.addStreamOutput(capture, type: .screen, sampleHandlerQueue: capture.queue)
            diagnostic("READY h264-matroska-v1")
            guard readLine() == "start" else { return }
            try await capture.start()
            // Keep the cooperative executor free while waiting for parent stop/EOF.
            await Task.detached { _ = readLine() }.value
            try await capture.stop()
        } catch { fail("\(error)") }
    }
}
