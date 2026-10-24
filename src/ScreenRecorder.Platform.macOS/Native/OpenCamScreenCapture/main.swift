// SPDX-License-Identifier: AGPL-3.0-or-later
import Foundation
import ScreenCaptureKit
import CoreMedia
import CoreVideo
import VideoToolbox
import CoreGraphics
import Darwin

let diagnosticLock = NSLock()
func diagnostic(_ text: String) { diagnosticLock.withLock { FileHandle.standardError.write(Data((text + "\n").utf8)) } }
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
        // Keep each expression small for the Swift compiler used by CI.
        var track = Data()
        track.append(uint(0xD7, 1))
        track.append(uint(0x73C5, 1))
        track.append(uint(0x83, 1))
        track.append(uint(0x9C, 0))
        track.append(text(0x86, "V_MPEG4/ISO/AVC"))
        track.append(element(0x63A2, avcc))
        track.append(uint(0x23E383, UInt64(1_000_000_000 / fps)))
        track.append(element(0xE0, video))
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

// Retained only until the corresponding VideoToolbox callback (at most three).
// Arrival time on stderr is deliberately not part of this protocol.
final class NativeFrameTiming {
    let presentedUs: Int64, acquiredUs: Int64, sequence: UInt64
    init(presentedUs: Int64, acquiredUs: Int64, sequence: UInt64) {
        self.presentedUs = presentedUs; self.acquiredUs = acquiredUs; self.sequence = sequence
    }
}
func microseconds(_ time: CMTime) -> Int64 {
    CMTimeConvertScale(time, timescale: 1_000_000, method: .roundTowardZero).value
}

// Quartz event locations and CGDisplayBounds share global, top-left display
// coordinates in points. Convert to physical pixels before subtracting the crop.
// Never retain or emit a global position after this privacy boundary.
struct PointerProjection {
    let displayBounds: CGRect
    let displayWidth: Int, displayHeight: Int
    let crop: CGRect
    func fields(point: CGPoint, buttons: Int) -> String {
        let x = (point.x - displayBounds.minX) * Double(displayWidth) / displayBounds.width - crop.minX
        let y = (point.y - displayBounds.minY) * Double(displayHeight) / displayBounds.height - crop.minY
        guard x.isFinite, y.isFinite, x >= 0, y >= 0, x < crop.width, y < crop.height else {
            return "0 0 0 0"
        }
        return "\(Int(x.rounded(.down))) \(Int(y.rounded(.down))) 1 \(buttons & 7)"
    }
}

struct PointerTimeline {
    let projection: PointerProjection
    private var lastUs: Int64 = -1
    private var lastPollUs: Int64?
    init(projection: PointerProjection) { self.projection = projection }
    mutating func record(point: CGPoint, hostUs: Int64, buttons: Int, transition: Bool) -> String? {
        // Ceiling(1e6/120): no burst/catch-up samples after a delayed timer.
        if !transition {
            if let lastPollUs, hostUs - lastPollUs < 8334 { return nil }
            lastPollUs = hostUs
        }
        let timestamp = max(max(0, hostUs), lastUs + 1)
        lastUs = timestamp
        return "POINTER v1 \(timestamp) \(projection.fields(point: point, buttons: buttons))"
    }
}

struct PointerButtons {
    static let eventMask: CGEventMask = [CGEventType.leftMouseDown, .leftMouseUp,
        .rightMouseDown, .rightMouseUp, .otherMouseDown, .otherMouseUp]
        .reduce(CGEventMask(0)) { $0 | (CGEventMask(1) << $1.rawValue) }
    var mask: Int
    mutating func apply(type: CGEventType, buttonNumber: Int64) -> Bool {
        let bit: Int, down: Bool
        switch type {
        case .leftMouseDown: bit = 1; down = true
        case .leftMouseUp: bit = 1; down = false
        case .rightMouseDown: bit = 2; down = true
        case .rightMouseUp: bit = 2; down = false
        case .otherMouseDown where buttonNumber == 2: bit = 4; down = true
        case .otherMouseUp where buttonNumber == 2: bit = 4; down = false
        default: return false
        }
        if down { mask |= bit } else { mask &= ~bit }
        return true
    }
}

// One bounded ring, one writer thread. Event callbacks never wait for stderr or
// enqueue an unbounded number of Dispatch blocks. Any lost record permanently
// invalidates this pointer track, while leaving the video encoder running.
final class PointerOutputBuffer {
    private let condition = NSCondition()
    private var ring: [String?]
    private var head = 0, count = 0
    private var closed = false
    private var error: String?
    init(capacity: Int = 512) { precondition(capacity > 0); ring = Array(repeating: nil, count: capacity) }
    func append(_ record: String) {
        condition.lock(); defer { condition.unlock() }
        guard !closed else { return }
        guard count < ring.count else {
            error = "POINTER_ERROR v1 queue-overflow"; closed = true; condition.signal(); return
        }
        ring[(head + count) % ring.count] = record; count += 1; condition.signal()
    }
    func markIncomplete(_ reason: String) {
        condition.lock(); defer { condition.unlock() }
        guard !closed else { return }
        error = "POINTER_ERROR v1 \(reason)"; closed = true; condition.signal()
    }
    func finish() { condition.lock(); closed = true; condition.signal(); condition.unlock() }
    func next() -> String? {
        condition.lock(); defer { condition.unlock() }
        while count == 0 && !closed { condition.wait() }
        if count > 0 {
            let record = ring[head]; ring[head] = nil
            head = (head + 1) % ring.count; count -= 1
            return record
        }
        let finalError = error; error = nil; return finalError
    }
}

final class PointerSampler {
    private var timeline: PointerTimeline
    private var buttons = PointerButtons(mask: 0)
    private let output = PointerOutputBuffer()
    private var tap: CFMachPort?
    private var source: CFRunLoopSource?
    private var runLoop: CFRunLoop?
    private let producer = DispatchGroup(), writer = DispatchGroup()
    private var started = false, stopped = false

    init(projection: PointerProjection) throws {
        timeline = PointerTimeline(projection: projection)
        // Intentionally mouse buttons only: no keyboard, flags, scroll, or motion
        // events. A listen-only session tap never modifies normal desktop input.
        tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .tailAppendEventTap,
            options: .listenOnly, eventsOfInterest: PointerButtons.eventMask, callback: { _, type, event, context in
                if let context {
                    Unmanaged<PointerSampler>.fromOpaque(context).takeUnretainedValue().handle(type: type, event: event)
                }
                return Unmanaged.passUnretained(event)
            }, userInfo: Unmanaged.passUnretained(self).toOpaque())
        guard let tap else {
            throw NSError(domain: "PointerPreparation", code: 1, userInfo: [NSLocalizedDescriptionKey:
                "Cannot prepare mouse-button monitoring. Check macOS Input Monitoring permission and retry; no keyboard events are collected."])
        }
        CGEvent.tapEnable(tap: tap, enable: false)
        guard let source = CFMachPortCreateRunLoopSource(nil, tap, 0) else {
            CFMachPortInvalidate(tap)
            throw NSError(domain: "PointerPreparation", code: 2, userInfo: [NSLocalizedDescriptionKey:
                "Cannot prepare mouse-button event run loop."])
        }
        self.source = source
    }

    func start() {
        precondition(!started && !stopped)
        started = true
        writer.enter()
        Thread.detachNewThread { [self] in
            defer { writer.leave() }
            while let record = output.next() { diagnostic(record) }
        }
        let ready = DispatchSemaphore(value: 0)
        producer.enter()
        Thread.detachNewThread { [self] in
            defer { producer.leave() }
            let loop = CFRunLoopGetCurrent()!
            runLoop = loop
            CFRunLoopAddSource(loop, source, .defaultMode)
            // Enable before seeding, so transitions during the baseline query
            // remain queued for this run loop instead of leaving a stale button.
            CGEvent.tapEnable(tap: tap!, enable: true)
            // Seed held buttons once. Every subsequent transition comes from the
            // event tap, so a down/up between position ticks cannot disappear.
            for (button, bit) in [(CGMouseButton.left, 1), (.right, 2), (.center, 4)] {
                if CGEventSource.buttonState(.combinedSessionState, button: button) { buttons.mask |= bit }
            }
            poll()
            let timer = CFRunLoopTimerCreateWithHandler(nil, CFAbsoluteTimeGetCurrent() + 1.0 / 120,
                1.0 / 120, 0, 0) { [weak self] _ in self?.poll() }!
            CFRunLoopAddTimer(loop, timer, .defaultMode)
            ready.signal()
            CFRunLoopRun()
            CGEvent.tapEnable(tap: tap!, enable: false)
            CFRunLoopTimerInvalidate(timer)
            // A stop block can run before a ready Mach-port source. Drain the
            // already-delivered button messages after disabling new delivery.
            // Bound even this shutdown work; never claim a complete track if
            // the source cannot be drained within the same queue budget.
            var drained = 0
            while CFRunLoopRunInMode(.defaultMode, 0.001, true) == .handledSource {
                drained += 1
                if drained >= 512 { output.markIncomplete("event-drain-limit"); break }
            }
            CFRunLoopRemoveSource(loop, source, .defaultMode)
            CFMachPortInvalidate(tap!)
        }
        ready.wait()
    }

    private func poll() {
        guard CGEvent.tapIsEnabled(tap: tap!) else { output.markIncomplete("event-tap-disabled"); return }
        guard let point = CGEvent(source: nil)?.location else { output.markIncomplete("position-unavailable"); return }
        record(point: point, transition: false)
    }
    private func handle(type: CGEventType, event: CGEvent) {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            output.markIncomplete("event-tap-disabled"); return
        }
        guard buttons.apply(type: type, buttonNumber: event.getIntegerValueField(.mouseEventButtonNumber)) else { return }
        record(point: event.location, transition: true)
    }
    private func record(point: CGPoint, transition: Bool) {
        // Use exactly the same CM host clock as FRAME_CLOCK. Event timestamps
        // use a different unit; stderr delivery time is not a media clock.
        let now = microseconds(CMClockGetTime(CMClockGetHostTimeClock()))
        if let record = timeline.record(point: point, hostUs: now, buttons: buttons.mask, transition: transition) {
            output.append(record)
        }
    }
    func stop() {
        guard !stopped else { return }
        stopped = true
        if started, let runLoop {
            CFRunLoopPerformBlock(runLoop, CFRunLoopMode.defaultMode.rawValue) { CFRunLoopStop(runLoop) }
            CFRunLoopWakeUp(runLoop)
            producer.wait() // no callback can enqueue after this join
            output.finish()
            writer.wait() // flush every sanitized record and any incomplete marker
        } else if let tap { CFMachPortInvalidate(tap) }
        source = nil; tap = nil; runLoop = nil
    }
    deinit { stop() }
}

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
            outputCallback: { context, frameContext, status, flags, sample in
                let timing = frameContext.map { Unmanaged<NativeFrameTiming>.fromOpaque($0).takeRetainedValue() }
                guard let context else { return }
                let encoder = Unmanaged<Encoder>.fromOpaque(context).takeUnretainedValue()
                defer { encoder.lock.lock(); encoder.pending -= 1; encoder.lock.unlock() }
                if status != noErr { fail("VideoToolbox encode status \(status)") }
                if flags.contains(.frameDropped) { encoder.lock.lock(); encoder.dropped += 1; encoder.lock.unlock(); return }
                guard let sample else { fail("VideoToolbox returned no sample") }
                do { try encoder.write(sample, timing: timing) } catch { fail("Encoded output: \(error)") }
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
    func encode(_ pixel: CVPixelBuffer, time: CMTime, timing: NativeFrameTiming? = nil) throws {
        lock.lock()
        if pending >= 3 { dropped += 1; lock.unlock(); return }
        pending += 1; lock.unlock()
        let frameContext = timing.map { Unmanaged.passRetained($0).toOpaque() }
        let status = VTCompressionSessionEncodeFrame(session!, imageBuffer: pixel,
            presentationTimeStamp: time, duration: CMTime(value: 1, timescale: Int32(fps)),
            frameProperties: nil, sourceFrameRefcon: frameContext, infoFlagsOut: nil)
        if status != noErr {
            if let frameContext { Unmanaged<NativeFrameTiming>.fromOpaque(frameContext).release() }
            lock.lock(); pending -= 1; lock.unlock(); try checked(status, "VTEncode")
        }
    }
    private func write(_ sample: CMSampleBuffer, timing: NativeFrameTiming?) throws {
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
        if let timing {
            guard timing.acquiredUs >= 0, timing.presentedUs >= timing.acquiredUs else { throw NSError(domain: "NativeClock", code: -1) }
            // Match the quantized Matroska packet PTS, not the unquantized encoder input.
            diagnostic("FRAME_CLOCK v1 \(pts * 1000) \(timing.presentedUs) \(timing.acquiredUs) \(timing.sequence)")
        }
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
    var latestAcquired: CMTime?
    let emitClock: Bool
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
    init(encoder: Encoder, display: CGDirectDisplayID, displayWidth: Int, displayHeight: Int, emitClock: Bool = false) {
        self.encoder = encoder; self.displayID = display
        self.displayWidth = displayWidth; self.displayHeight = displayHeight
        self.emitClock = emitClock
        self.initialBounds = CGDisplayBounds(display)
        self.initialRotation = CGDisplayRotation(display)
    }
    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen, sampleBuffer.isValid,
            let info = (CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]])?.first,
            let status = info[.status] as? Int, status == SCFrameStatus.complete.rawValue,
            let image = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        var acquired: CMTime?
        if emitClock {
            guard let displayTime = info[.displayTime] as? NSNumber else { fail("Frame acquisition clock missing") }
            acquired = CMClockMakeHostTimeFromSystemUnits(displayTime.uint64Value)
        }
        lock.lock(); latest = image; latestAcquired = acquired; capturedFrames += 1; lock.unlock()
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
            lock.lock(); let image = latest; let sequence = capturedFrames; let acquired = latestAcquired; lock.unlock()
            guard let image else { return }
            if sequence == sampledSequence { repeatedSamples += 1 }
            sampledSequence = sequence
            let now = CMClockGetTime(CMClockGetHostTimeClock())
            if origin == nil { origin = now }
            let timing = acquired.map { NativeFrameTiming(presentedUs: microseconds(now), acquiredUs: microseconds($0), sequence: sequence) }
            do { try encoder.encode(image, time: CMTimeSubtract(now, origin!), timing: timing) }
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

#if !POINTER_TESTS
@main struct Main {
    static func main() async {
        signal(SIGPIPE, SIG_IGN)
        do {
            let args = Array(CommandLine.arguments.dropFirst())
            if args == ["--synthetic-test"] || args == ["--synthetic-clock-test"] {
                let emitClock = args == ["--synthetic-clock-test"]
                let encoder = try Encoder(width: 320, height: 180, fps: 30, bitrate: 1000)
                for index in 0..<60 {
                    var pixel: CVPixelBuffer?
                    try checked(CVPixelBufferCreate(nil, 320, 180, kCVPixelFormatType_32BGRA,
                        [kCVPixelBufferIOSurfacePropertiesKey: [:]] as CFDictionary, &pixel), "PixelBuffer")
                    CVPixelBufferLockBaseAddress(pixel!, [])
                    memset(CVPixelBufferGetBaseAddress(pixel!), Int32(index * 3), CVPixelBufferGetDataSize(pixel!))
                    CVPixelBufferUnlockBaseAddress(pixel!, [])
                    // A deliberate gap verifies that elapsed timestamps survive muxing.
                    let time = CMTime(value: Int64(index + (index >= 30 ? 15 : 0)), timescale: 30)
                    let presented = 8_000_000 + microseconds(time)
                    let timing = emitClock ? NativeFrameTiming(presentedUs: presented, acquiredUs: presented - 3333, sequence: UInt64(index + 1)) : nil
                    try encoder.encode(pixel!, time: time, timing: timing)
                    try checked(VTCompressionSessionCompleteFrames(encoder.session!, untilPresentationTimeStamp: .invalid), "TestDrain")
                }
                try encoder.finish(); return
            }
            guard (args.count == 10 || args.count == 11), args.allSatisfy({ Int($0) != nil }) else { fail("Invalid arguments") }
            let v = args.map { Int($0)! }
            let emitClock = v.count == 11 && v[10] == 1
            if v.count == 11 && v[10] != 1 { fail("Invalid clock protocol") }
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
            config.queueDepth = 3; config.showsCursor = !emitClock && v[9] == 1
            config.pixelFormat = kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange
            config.colorSpaceName = CGColorSpace.sRGB
            config.capturesAudio = false
            // Prepare before READY: cursor-free capture must never start without
            // a working mouse-button observer. This does not request permission.
            let pointer = emitClock ? try PointerSampler(projection: PointerProjection(displayBounds: displayBounds,
                displayWidth: v[5], displayHeight: v[6],
                crop: CGRect(x: v[1], y: v[2], width: v[3], height: v[4]))) : nil
            defer { pointer?.stop() }
            let encoder = try Encoder(width: v[3], height: v[4], fps: v[7], bitrate: v[8])
            let capture = Capture(encoder: encoder, display: id, displayWidth: v[5], displayHeight: v[6], emitClock: emitClock)
            let filter = SCContentFilter(display: display, excludingWindows: [])
            capture.stream = SCStream(filter: filter, configuration: config, delegate: capture)
            try capture.stream!.addStreamOutput(capture, type: .screen, sampleHandlerQueue: capture.queue)
            diagnostic(emitClock ? "READY h264-matroska-clock-v1" : "READY h264-matroska-v1")
            guard readLine() == "start" else { return }
            pointer?.start()
            try await capture.start()
            // Keep the cooperative executor free while waiting for parent stop/EOF.
            await Task.detached { _ = readLine() }.value
            pointer?.stop()
            try await capture.stop()
        } catch { fail("\(error)") }
    }
}
#endif
