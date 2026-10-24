// SPDX-License-Identifier: AGPL-3.0-or-later
import Foundation
import CoreGraphics

@main struct PointerSamplerTests {
    static func main() {
        // A Y flip, missing Retina scale, or applying crop before display origin
        // breaks these hand-derived pixel positions.
        let projection = PointerProjection(displayBounds: CGRect(x: -1440, y: -900, width: 1440, height: 900),
            displayWidth: 2880, displayHeight: 1800, crop: CGRect(x: 200, y: 100, width: 600, height: 400))
        var timeline = PointerTimeline(projection: projection)
        assert(timeline.record(point: CGPoint(x: -1330, y: -840), hostUs: 100, buttons: 7, transition: false)
            == "POINTER v1 100 20 20 1 7")
        // Two transitions within a polling interval must both survive, including
        // identical clock readings after microsecond quantization.
        assert(timeline.record(point: CGPoint(x: -1330, y: -830), hostUs: 100, buttons: 1, transition: true)
            == "POINTER v1 101 20 40 1 1")
        assert(timeline.record(point: CGPoint(x: -1330, y: -830), hostUs: 101, buttons: 0, transition: true)
            == "POINTER v1 102 20 40 1 0")
        assert(timeline.record(point: CGPoint(x: -1330, y: -830), hostUs: 8433, buttons: 0, transition: false) == nil)
        assert(timeline.record(point: CGPoint(x: -1330, y: -830), hostUs: 8434, buttons: 0, transition: false)
            == "POINTER v1 8434 20 40 1 0")
        let cases: [(CGPoint, String)] = [
            (CGPoint(x: -1340, y: -850), "0 0 1 7"),
            (CGPoint(x: -1040.01, y: -650.01), "599 399 1 7"),
            (CGPoint(x: -1040, y: -700), "0 0 0 0"),
            (CGPoint(x: -1200, y: -650), "0 0 0 0"),
            (CGPoint(x: -1340.01, y: -800), "0 0 0 0"),
            (CGPoint(x: -1200, y: -850.01), "0 0 0 0"),
            (CGPoint(x: 9999, y: -5000), "0 0 0 0"),
            (CGPoint(x: CGFloat.nan, y: 0), "0 0 0 0")
        ]
        for (index, fixture) in cases.enumerated() {
            let time = Int64(20_000 + index)
            assert(timeline.record(point: fixture.0, hostUs: time, buttons: 7, transition: true)
                == "POINTER v1 \(time) \(fixture.1)")
        }
        let standard = PointerProjection(displayBounds: CGRect(x: 1920, y: 0, width: 1920, height: 1080),
            displayWidth: 1920, displayHeight: 1080, crop: CGRect(x: 0, y: 0, width: 1920, height: 1080))
        var standardTimeline = PointerTimeline(projection: standard)
        assert(standardTimeline.record(point: CGPoint(x: 1924.9, y: 6.1), hostUs: 1, buttons: 0, transition: false)
            == "POINTER v1 1 4 6 1 0")

        var buttons = PointerButtons(mask: 0)
        let forbidden: [CGEventType] = [.keyDown, .keyUp, .flagsChanged, .mouseMoved, .scrollWheel]
        for type in forbidden { assert(PointerButtons.eventMask & (CGEventMask(1) << type.rawValue) == 0) }
        for type in [CGEventType.leftMouseDown, .leftMouseUp, .rightMouseDown, .rightMouseUp, .otherMouseDown, .otherMouseUp] {
            assert(PointerButtons.eventMask & (CGEventMask(1) << type.rawValue) != 0)
        }
        assert(buttons.apply(type: .leftMouseDown, buttonNumber: 0) && buttons.mask == 1)
        assert(buttons.apply(type: .rightMouseDown, buttonNumber: 1) && buttons.mask == 3)
        assert(buttons.apply(type: .otherMouseDown, buttonNumber: 2) && buttons.mask == 7)
        assert(!buttons.apply(type: .otherMouseDown, buttonNumber: 3) && buttons.mask == 7)
        assert(!buttons.apply(type: .keyDown, buttonNumber: 0) && buttons.mask == 7)
        assert(buttons.apply(type: .leftMouseUp, buttonNumber: 0) && buttons.mask == 6)
        assert(buttons.apply(type: .rightMouseUp, buttonNumber: 1) && buttons.mask == 4)
        assert(buttons.apply(type: .otherMouseUp, buttonNumber: 2) && buttons.mask == 0)

        let buffer = PointerOutputBuffer(capacity: 2)
        buffer.append("first"); buffer.append("second"); buffer.append("lost-transition")
        buffer.append("must-not-resume-after-data-loss")
        assert(buffer.next() == "first")
        assert(buffer.next() == "second")
        assert(buffer.next() == "POINTER_ERROR v1 queue-overflow")
        assert(buffer.next() == nil)
        let normal = PointerOutputBuffer(capacity: 2)
        normal.append("first"); normal.append("second")
        assert(normal.next() == "first")
        normal.append("wrap-around"); normal.finish()
        assert(normal.next() == "second" && normal.next() == "wrap-around" && normal.next() == nil)
        let waiting = PointerOutputBuffer(capacity: 2)
        let reader = DispatchGroup(); reader.enter()
        Thread.detachNewThread { assert(waiting.next() == nil); reader.leave() }
        waiting.finish()
        assert(reader.wait(timeout: .now() + 2) == .success, "stop must wake and join a waiting writer")
        let disabled = PointerOutputBuffer(capacity: 2)
        disabled.markIncomplete("event-tap-disabled")
        assert(disabled.next() == "POINTER_ERROR v1 event-tap-disabled" && disabled.next() == nil)
        print("Pointer projection / privacy / rate / transitions / bounded drain tests passed.")
    }
}
