// SPDX-License-Identifier: AGPL-3.0-or-later
import AppKit

private enum OverlayMode: String {
    case halo
    case ripple
}

private func parseMode() -> OverlayMode? {
    let arguments = CommandLine.arguments
    guard arguments.count == 3,
          arguments[1] == "--mode" else {
        return nil
    }
    return OverlayMode(rawValue: arguments[2])
}

private final class CursorOverlayView: NSView {
    var mode: OverlayMode = .halo
    private var rippleStartedAt: TimeInterval?

    override var isOpaque: Bool { false }

    func triggerRipple() {
        rippleStartedAt = ProcessInfo.processInfo.systemUptime
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)

        let center = NSPoint(x: bounds.midX, y: bounds.midY)
        let haloRect = NSRect(
            x: center.x - 21,
            y: center.y - 21,
            width: 42,
            height: 42)
        let halo = NSBezierPath(ovalIn: haloRect)
        NSColor.systemYellow.withAlphaComponent(0.18).setFill()
        halo.fill()
        NSColor.systemYellow.withAlphaComponent(0.92).setStroke()
        halo.lineWidth = 3
        halo.stroke()

        guard mode == .ripple,
              let startedAt = rippleStartedAt else {
            return
        }

        let elapsed = ProcessInfo.processInfo.systemUptime - startedAt
        if elapsed >= 0.55 {
            rippleStartedAt = nil
            return
        }

        let progress = elapsed / 0.55
        let radius = 25 + (32 * progress)
        let rippleRect = NSRect(
            x: center.x - radius,
            y: center.y - radius,
            width: radius * 2,
            height: radius * 2)
        let ripple = NSBezierPath(ovalIn: rippleRect)
        NSColor.systemYellow
            .withAlphaComponent(0.9 * (1 - progress))
            .setStroke()
        ripple.lineWidth = 4
        ripple.stroke()
    }
}

private final class CursorOverlayController: NSObject, NSApplicationDelegate {
    private let mode: OverlayMode
    private let overlaySize = NSSize(width: 124, height: 124)
    private var window: NSWindow?
    private var overlayView: CursorOverlayView?
    private var timer: Timer?
    private var wasPressed = false

    init(mode: OverlayMode) {
        self.mode = mode
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let window = NSWindow(
            contentRect: NSRect(origin: .zero, size: overlaySize),
            styleMask: [.borderless],
            backing: .buffered,
            defer: false)
        window.backgroundColor = .clear
        window.isOpaque = false
        window.hasShadow = false
        window.ignoresMouseEvents = true
        window.level = .screenSaver
        window.collectionBehavior = [
            .canJoinAllSpaces,
            .fullScreenAuxiliary,
            .stationary,
            .ignoresCycle
        ]

        let view = CursorOverlayView(frame: NSRect(origin: .zero, size: overlaySize))
        view.mode = mode
        window.contentView = view
        window.orderFrontRegardless()

        self.window = window
        self.overlayView = view
        updateOverlay()
        timer = Timer.scheduledTimer(
            timeInterval: 1.0 / 60.0,
            target: self,
            selector: #selector(updateOverlay),
            userInfo: nil,
            repeats: true)
    }

    @objc private func updateOverlay() {
        guard let window, let overlayView else { return }

        let cursor = NSEvent.mouseLocation
        window.setFrameOrigin(NSPoint(
            x: cursor.x - overlaySize.width / 2,
            y: cursor.y - overlaySize.height / 2))

        let isPressed = NSEvent.pressedMouseButtons & 1 != 0
        if mode == .ripple && isPressed && !wasPressed {
            overlayView.triggerRipple()
        }
        wasPressed = isPressed
        overlayView.needsDisplay = true
    }
}

guard let mode = parseMode() else {
    FileHandle.standardError.write(
        Data("usage: OpenCam.CursorOverlay --mode halo|ripple\n".utf8))
    exit(2)
}

private let application = NSApplication.shared
application.setActivationPolicy(.accessory)
private let controller = CursorOverlayController(mode: mode)
application.delegate = controller
application.run()
