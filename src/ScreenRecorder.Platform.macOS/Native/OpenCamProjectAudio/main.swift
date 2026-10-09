// SPDX-License-Identifier: AGPL-3.0-or-later
import Foundation
import AVFoundation

// Dedicated playback-only child. EOF drains; owner termination immediately releases output.
// No file paths, microphone access or capture permissions are accepted.
func run() throws {
    let engine = AVAudioEngine(), player = AVAudioPlayerNode()
    let format = AVAudioFormat(standardFormatWithSampleRate: 48_000, channels: 2)!
    let slots = DispatchSemaphore(value: 8), completion = DispatchGroup()
    let queue = DispatchQueue(label: "OpenCam.preview.clock")
    let state = NSLock()
    var queued = 0, started = false, paused = false, eof = false
    var submitted: Int64 = 0, played: Int64 = 0, offset: Int64 = 0
    func rawClock() -> Int64 {
        guard let time = player.lastRenderTime.flatMap({ player.playerTime(forNodeTime: $0) }) else { return 0 }
        return max(0, Int64(Double(time.sampleTime) * 48_000 / time.sampleRate))
    }
    let timer = DispatchSource.makeTimerSource(queue: queue)
    engine.attach(player)
    engine.connect(player, to: engine.mainMixerNode, format: format)
    timer.schedule(deadline: .now(), repeating: .milliseconds(5))
    timer.setEventHandler {
        state.lock()
        if started {
            let samples = paused ? played : min(submitted, max(played, rawClock() + offset))
            FileHandle.standardOutput.write(Data("{\"sampleClock\":\(samples)}\n".utf8))
        }
        state.unlock()
    }
    timer.resume()
    defer { timer.cancel(); queue.sync {}; player.stop(); engine.stop() }
    var blocks = 0
    while true {
        var bytes = Data()
        while bytes.count < 8192 {
            guard let part = try FileHandle.standardInput.read(upToCount: 8192 - bytes.count), !part.isEmpty else { break }
            bytes.append(part)
        }
        if bytes.isEmpty { break }
        guard bytes.count % 8 == 0, slots.wait(timeout: .now() + 5) == .success else {
            throw NSError(domain: "OpenCamProjectAudio", code: 1)
        }
        let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(bytes.count / 8))!
        buffer.frameLength = buffer.frameCapacity
        try bytes.withUnsafeBytes { data in
            for frame in 0..<Int(buffer.frameLength) {
                for channel in 0..<2 {
                    let value = Float(bitPattern: data.loadUnaligned(fromByteOffset: frame * 8 + channel * 4, as: UInt32.self).littleEndian)
                    guard value.isFinite else { throw NSError(domain: "OpenCamProjectAudio", code: 2) }
                    buffer.floatChannelData![channel][frame] = value
                }
            }
        }
        completion.enter()
        state.lock()
        queued += 1
        submitted += Int64(buffer.frameLength)
        let endSample = submitted
        player.scheduleBuffer(buffer, completionCallbackType: .dataPlayedBack) { _ in
            queue.async {
                state.lock()
                played = endSample
                queued -= 1
                if queued == 0 && !eof {
                    player.pause()
                    paused = true
                    offset = played - rawClock()
                }
                state.unlock()
                slots.signal()
                completion.leave()
            }
        }
        blocks += 1
        if (!started || paused) && queued >= 4 {
            do {
                if !started { try engine.start() }
                player.play(); started = true; paused = false
            } catch { state.unlock(); throw error }
        }
        state.unlock()
    }
    guard blocks > 0 else { throw NSError(domain: "OpenCamProjectAudio", code: 3) }
    state.lock()
    eof = true
    do {
        if !started { try engine.start() }
        if !started || paused { player.play(); started = true; paused = false }
    } catch { state.unlock(); throw error }
    state.unlock()
    guard completion.wait(timeout: .now() + 5) == .success else { throw NSError(domain: "OpenCamProjectAudio", code: 4) }
    // The final video deadline may lie between timer ticks. Publish the exact drained endpoint.
    queue.sync {
        state.lock()
        FileHandle.standardOutput.write(Data("{\"sampleClock\":\(played)}\n".utf8))
        state.unlock()
    }
}

do { try run() }
catch {
    FileHandle.standardError.write(Data("Preview audio failed: \(error.localizedDescription)\n".utf8))
    exit(1)
}
