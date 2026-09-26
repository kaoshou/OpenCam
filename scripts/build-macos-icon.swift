// SPDX-License-Identifier: AGPL-3.0-or-later
import AppKit
import Foundation

struct IconVariant {
    let filename: String
    let pixels: Int
}

let variants = [
    IconVariant(filename: "icon_16x16.png", pixels: 16),
    IconVariant(filename: "icon_16x16@2x.png", pixels: 32),
    IconVariant(filename: "icon_32x32.png", pixels: 32),
    IconVariant(filename: "icon_32x32@2x.png", pixels: 64),
    IconVariant(filename: "icon_128x128.png", pixels: 128),
    IconVariant(filename: "icon_128x128@2x.png", pixels: 256),
    IconVariant(filename: "icon_256x256.png", pixels: 256),
    IconVariant(filename: "icon_256x256@2x.png", pixels: 512),
    IconVariant(filename: "icon_512x512.png", pixels: 512),
    IconVariant(filename: "icon_512x512@2x.png", pixels: 1024),
]

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data("error: \(message)\n".utf8))
    Foundation.exit(1)
}

guard CommandLine.arguments.count == 3 else {
    fail("usage: build-macos-icon.swift source.png output.iconset")
}

let sourceURL = URL(fileURLWithPath: CommandLine.arguments[1])
let outputURL = URL(fileURLWithPath: CommandLine.arguments[2], isDirectory: true)
guard let source = NSImage(contentsOf: sourceURL),
      let sourceRepresentation = NSBitmapImageRep(data: try Data(contentsOf: sourceURL)) else {
    fail("cannot decode source PNG: \(sourceURL.path)")
}
guard sourceRepresentation.pixelsWide == 1024,
      sourceRepresentation.pixelsHigh == 1024,
      sourceRepresentation.hasAlpha else {
    fail("source PNG must be 1024x1024 with an alpha channel")
}

try FileManager.default.createDirectory(
    at: outputURL,
    withIntermediateDirectories: true)

for variant in variants {
    guard let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: variant.pixels,
        pixelsHigh: variant.pixels,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: variant.pixels * 4,
        bitsPerPixel: 32
    ), let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
        fail("cannot allocate RGBA bitmap for \(variant.filename)")
    }

    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = context
    context.imageInterpolation = .high
    context.cgContext.clear(CGRect(
        x: 0,
        y: 0,
        width: variant.pixels,
        height: variant.pixels))
    source.draw(
        in: NSRect(x: 0, y: 0, width: variant.pixels, height: variant.pixels),
        from: NSRect(origin: .zero, size: source.size),
        operation: .copy,
        fraction: 1,
        respectFlipped: false,
        hints: [.interpolation: NSImageInterpolation.high])
    context.flushGraphics()
    NSGraphicsContext.restoreGraphicsState()

    guard let png = bitmap.representation(using: .png, properties: [:]) else {
        fail("cannot encode \(variant.filename) as PNG")
    }
    do {
        try png.write(to: outputURL.appendingPathComponent(variant.filename), options: .atomic)
    } catch {
        fail("cannot write \(variant.filename): \(error.localizedDescription)")
    }
}
