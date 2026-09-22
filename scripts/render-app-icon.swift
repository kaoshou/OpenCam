// SPDX-License-Identifier: AGPL-3.0-or-later
// Usage: swift scripts/render-app-icon.swift <source.svg> <output.png>
import AppKit
import Foundation

guard CommandLine.arguments.count == 3 else {
    fatalError("usage: render-app-icon.swift <source.svg> <output.png>")
}

let source = CommandLine.arguments[1]
let output = CommandLine.arguments[2]
let size = 1024

guard let image = NSImage(contentsOfFile: source),
      let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: size,
        pixelsHigh: size,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: 0,
        bitsPerPixel: 0
      ),
      let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
    fatalError("could not load SVG or create bitmap")
}

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = context
context.imageInterpolation = .high
context.compositingOperation = .copy
NSColor.clear.setFill()
NSRect(x: 0, y: 0, width: size, height: size).fill()
image.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
context.flushGraphics()
NSGraphicsContext.restoreGraphicsState()

guard let png = bitmap.representation(using: .png, properties: [:]) else {
    fatalError("could not encode PNG")
}
try png.write(to: URL(fileURLWithPath: output), options: .atomic)
