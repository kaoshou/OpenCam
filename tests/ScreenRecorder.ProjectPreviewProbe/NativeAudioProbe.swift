// SPDX-License-Identifier: AGPL-3.0-or-later
import Foundation
import AVFoundation

// Standalone diagnostic, not a product helper. Does not open a microphone or capture other apps.
final class RenderEvidence {
    private let lock = NSLock()
    private var nonSilentFrames: Int64 = 0
    func observe(_ buffer: AVAudioPCMBuffer) {
        guard let samples = buffer.floatChannelData?[0] else { return }
        var count: Int64 = 0
        for index in 0..<Int(buffer.frameLength) {
            if abs(samples[index]) > 0.00001 { count += 1 }
        }
        lock.lock(); nonSilentFrames += count; lock.unlock()
    }
    var frames: Int64 {
        lock.lock(); defer { lock.unlock() }; return nonSilentFrames
    }
}

func pump(_ duration: TimeInterval) {
    RunLoop.current.run(until: Date(timeIntervalSinceNow: duration))
}

func check(_ condition: Bool, _ message: String) throws {
    if !condition { throw NSError(domain: "OpenCamAudioProbe", code: 1, userInfo: [NSLocalizedDescriptionKey: message]) }
}

func selfTest() throws -> [String: Any] {
    let engine = AVAudioEngine()
    let player = AVAudioPlayerNode()
    let format = AVAudioFormat(standardFormatWithSampleRate: 48_000, channels: 1)!
    let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 48_000)!
    buffer.frameLength = 48_000
    // Quiet synthetic signal; never change the user's system volume.
    for index in 0..<48_000 {
        buffer.floatChannelData![0][index] = Float(sin(2 * Double.pi * 440 * Double(index) / 48_000) * 0.01)
    }
    let evidence = RenderEvidence()
    engine.attach(player)
    engine.connect(player, to: engine.mainMixerNode, format: format)
    engine.mainMixerNode.installTap(onBus: 0, bufferSize: 1024, format: nil) { pcm, _ in evidence.observe(pcm) }
    defer {
        player.stop(); engine.stop()
        engine.mainMixerNode.removeTap(onBus: 0)
    }
    player.scheduleBuffer(buffer)
    try engine.start()
    player.play()
    pump(0.35)
    let played = evidence.frames
    let clock = player.lastRenderTime.flatMap { player.playerTime(forNodeTime: $0) }?.sampleTime ?? 0
    try check(played > 0 && clock > 0, "Output device did not render nonzero samples or advance its sample clock")
    player.stop()
    engine.stop()
    let stoppedAt = evidence.frames
    pump(0.25)
    let stopped = !player.isPlaying && !engine.isRunning && evidence.frames == stoppedAt
    try check(stopped, "Output continued rendering after stop returned")
    player.scheduleBuffer(buffer)
    try engine.start()
    player.play()
    pump(0.25)
    let restarted = evidence.frames > stoppedAt
    player.stop(); engine.stop()
    let secondStoppedAt = evidence.frames
    pump(0.25)
    try check(restarted && !engine.isRunning && !player.isPlaying && evidence.frames == secondStoppedAt,
              "Restart or second stop failed")
    return ["playedFrames": played, "sampleClock": clock, "stopped": stopped,
            "restartPassed": restarted, "status": "PASS",
            "scope": "Real default output device; no acoustic loopback or integrated video/recording test"]
}

// Feed actual FFmpeg-decoded PCM and RGBA packets through a device-clock publication loop.
// This diagnostic deliberately does not claim visible GUI rendering or streaming decode.
func integrated(_ path: String) throws -> [String: Any] {
    let encoded = try Data(contentsOf: URL(fileURLWithPath: path))
    try check(encoded.count <= 4 * 1024 * 1024, "Diagnostic input quota exceeded")
    guard let input = try JSONSerialization.jsonObject(with: encoded) as? [String: Any],
          let pcm64 = input["pcm"] as? String, let pcm = Data(base64Encoded: pcm64),
          let rows = input["frames"] as? [[String: Any]],
          let width = input["width"] as? Int, let height = input["height"] as? Int
    else { throw NSError(domain: "OpenCamAudioProbe", code: 2) }
    try check(width == 64 && height == 36 && rows.count > 0 && rows.count <= 240 &&
              pcm.count > 0 && pcm.count <= 4 * 48_000 * 4 && pcm.count % 4 == 0, "Invalid bounded fixture")
    var frames: [(Double, Data, Int)] = []
    for row in rows {
        guard let seconds = row["seconds"] as? Double, seconds.isFinite,
              let pixels64 = row["rgba"] as? String, let pixels = Data(base64Encoded: pixels64),
              let clip = row["clip"] as? Int else { throw NSError(domain: "OpenCamAudioProbe", code: 3) }
        try check(seconds >= 0 && seconds < Double(pcm.count / 4) / 48_000 &&
                  pixels.count == width * height * 4 &&
                  (frames.last == nil || seconds > frames.last!.0), "Invalid frame timestamps or payload")
        frames.append((seconds, pixels, clip))
    }
    let engine = AVAudioEngine()
    let player = AVAudioPlayerNode()
    let format = AVAudioFormat(standardFormatWithSampleRate: 48_000, channels: 1)!
    let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(pcm.count / 4))!
    buffer.frameLength = buffer.frameCapacity
    pcm.withUnsafeBytes { bytes in
        for index in 0..<Int(buffer.frameLength) {
            buffer.floatChannelData![0][index] = Float(bitPattern: bytes.loadUnaligned(fromByteOffset: index * 4, as: UInt32.self).littleEndian)
        }
    }
    for index in 0..<Int(buffer.frameLength) {
        try check(buffer.floatChannelData![0][index].isFinite, "Non-finite PCM")
    }
    let evidence = RenderEvidence()
    engine.attach(player)
    engine.connect(player, to: engine.mainMixerNode, format: format)
    engine.mainMixerNode.installTap(onBus: 0, bufferSize: 256, format: nil) { pcm, _ in evidence.observe(pcm) }
    defer { player.stop(); engine.stop(); engine.mainMixerNode.removeTap(onBus: 0) }
    var published = 0, crossedBoundary = false
    var maxLate: Double = 0, maxStop: Double = 0
    var lastPacket: Data? = nil
    func play(_ until: Double) throws {
        var index = 0
        let deadline = ProcessInfo.processInfo.systemUptime + until + 2
        player.scheduleBuffer(buffer)
        try engine.start()
        player.play()
        while ProcessInfo.processInfo.systemUptime < deadline {
            guard let render = player.lastRenderTime,
                  let time = player.playerTime(forNodeTime: render) else { pump(0.002); continue }
            let clock = Double(time.sampleTime) / time.sampleRate
            while index < frames.count && frames[index].0 <= clock && frames[index].0 < until {
                // Publish the actual decoded packet, not a synthetic timeline counter.
                lastPacket = frames[index].1
                crossedBoundary = crossedBoundary || frames[index].2 == 1
                maxLate = max(maxLate, clock - frames[index].0)
                published += 1; index += 1
            }
            if clock >= until { return }
            pump(0.002)
        }
        throw NSError(domain: "OpenCamAudioProbe", code: 4, userInfo: [NSLocalizedDescriptionKey: "Device sample clock stalled"])
    }
    func stopAndObserve() throws {
        let start = ProcessInfo.processInfo.systemUptime
        player.stop(); engine.stop()
        maxStop = max(maxStop, ProcessInfo.processInfo.systemUptime - start)
        let audio = evidence.frames, video = published
        pump(0.25)
        try check(!engine.isRunning && !player.isPlaying && evidence.frames == audio && published == video,
                  "Audio or video publication continued after stop")
    }
    try play(0.6)
    try stopAndObserve()
    let firstCount = published
    try play(Double(buffer.frameLength) / 48_000)
    try stopAndObserve()
    try check(evidence.frames > 0 && published > firstCount && crossedBoundary && lastPacket != nil,
              "Decoded playback did not render audio, restart, or cross clips")
    try check(maxLate <= 0.04 && maxStop <= 0.25, "Device-clock scheduling/stop threshold exceeded")
    return ["status": "PASS", "publishedFrames": published, "renderedNonSilentFrames": evidence.frames,
            "crossedClipBoundary": crossedBoundary, "maximumFrameLatenessMs": maxLate * 1000,
            "maximumStopMs": maxStop * 1000, "restartAndStop": true,
            "scope": "Actual decoded packets scheduled by real output sample clock; no acoustic loopback or GUI-render claim"]
}

do {
    let args = Array(CommandLine.arguments.dropFirst())
    let result: [String: Any]
    if args == ["--self-test"] { result = try selfTest() }
    else if args.count == 2 && args[0] == "--integrated" { result = try integrated(args[1]) }
    else { throw NSError(domain: "OpenCamAudioProbe", code: 5, userInfo: [NSLocalizedDescriptionKey: "Required: --self-test or --integrated fixture.json"]) }
    let data = try JSONSerialization.data(withJSONObject: result, options: [.sortedKeys])
    print(String(decoding: data, as: UTF8.self))
} catch {
    let data = try! JSONSerialization.data(withJSONObject: ["status": "FAIL", "reason": error.localizedDescription])
    print(String(decoding: data, as: UTF8.self))
    exit(1)
}
