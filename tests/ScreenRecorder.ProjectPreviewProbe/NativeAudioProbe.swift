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

do {
    try check(Array(CommandLine.arguments.dropFirst()) == ["--self-test"], "Required: --self-test")
    let data = try JSONSerialization.data(withJSONObject: selfTest(), options: [.sortedKeys])
    print(String(decoding: data, as: UTF8.self))
} catch {
    let data = try! JSONSerialization.data(withJSONObject: ["status": "FAIL", "reason": error.localizedDescription])
    print(String(decoding: data, as: UTF8.self))
    exit(1)
}
