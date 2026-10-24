// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;

namespace ScreenRecorder.Core.Models;

public sealed record TeachingPointerBitmap(ReadOnlyMemory<byte> Pixels, int Width, int Height,
    double HotspotX, double HotspotY, double VisibleExtent);

/// <summary>Same checked-in transparent artwork for Windows, AppKit and UI thumbnails.</summary>
public static class TeachingPointerArtwork
{
    private const int SourceWidth = 280, SourceHeight = 240;
    private static readonly ConcurrentDictionary<TeachingPointerOptions, TeachingPointerBitmap> Cache = new();
    public static Stream OpenPng(TeachingPointerStyle style)
    {
        if (!Enum.IsDefined(style)) style = TeachingPointerStyle.WarmIvory;
        return typeof(TeachingPointerArtwork).Assembly.GetManifestResourceStream(
            $"ScreenRecorder.Core.Assets.TeachingPointers.{style}.png")
            ?? throw new InvalidDataException("Teaching pointer image missing.");
    }
    public static TeachingPointerBitmap Get(TeachingPointerOptions options) =>
        Cache.GetOrAdd(TeachingPointerOptions.Normalize(options), Build);

    private static TeachingPointerBitmap Build(TeachingPointerOptions options)
    {
        using var resource = OpenPng(options.Style);
        var source = Decode(resource);
        var scale = options.Scale;
        var width = (int)Math.Ceiling(140 * scale); var height = (int)Math.Ceiling(120 * scale);
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sx = (x + .5) * 2 / scale - .5; var sy = (y + .5) * 2 / scale - .5;
            var left = (int)Math.Floor(sx); var top = (int)Math.Floor(sy);
            double alpha = 0, red = 0, green = 0, blue = 0;
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
            {
                var px = left + dx; var py = top + dy;
                if (px < 0 || py < 0 || px >= SourceWidth || py >= SourceHeight) continue;
                var weight = (dx == 0 ? 1 - (sx - left) : sx - left) * (dy == 0 ? 1 - (sy - top) : sy - top);
                var i = (py * SourceWidth + px) * 4; var a = source[i + 3] * weight;
                alpha += a; red += source[i] * a; green += source[i + 1] * a; blue += source[i + 2] * a;
            }
            var target = (y * width + x) * 4;
            if (alpha <= 0) continue;
            pixels[target] = (byte)Math.Clamp(Math.Round(red / alpha), 0, 255);
            pixels[target+1] = (byte)Math.Clamp(Math.Round(green / alpha), 0, 255);
            pixels[target+2] = (byte)Math.Clamp(Math.Round(blue / alpha), 0, 255);
            pixels[target+3] = (byte)Math.Clamp(Math.Round(alpha), 0, 255);
        }
        var extent = options.Style switch { TeachingPointerStyle.RoundShort => 76, TeachingPointerStyle.HighContrast => 101, _ => 96 };
        return new(pixels, width, height, 16 * scale, 16 * scale, extent * scale);
    }

    // Only our bounded, generated 8-bit RGBA PNG resources are accepted. Legacy artwork stays untouched.
    private static byte[] Decode(Stream resource)
    {
        using var png = new MemoryStream(); resource.CopyTo(png);
        var bytes = png.ToArray();
        if (bytes.Length < 33 || !bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))
            throw new InvalidDataException("Invalid teaching pointer PNG.");
        using var compressed = new MemoryStream();
        var header = false;
        for (var offset = 8; offset + 12 <= bytes.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset,4)));
            if (length < 0 || length > bytes.Length - offset - 12) throw new InvalidDataException("Invalid PNG chunk.");
            var kind = System.Text.Encoding.ASCII.GetString(bytes, offset+4,4);
            if (kind == "IHDR")
            {
                if (length != 13 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset+8,4)) != SourceWidth ||
                    BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset+12,4)) != SourceHeight ||
                    bytes[offset+16] != 8 || bytes[offset+17] != 6 ||
                    bytes[offset+18] != 0 || bytes[offset+19] != 0 || bytes[offset+20] != 0)
                    throw new InvalidDataException("Unsupported teaching pointer PNG.");
                header = true;
            }
            if (kind == "IDAT") compressed.Write(bytes,offset+8,length);
            offset += length+12;
        }
        if (!header) throw new InvalidDataException("Missing PNG header.");
        compressed.Position=0;
        using var inflate = new ZLibStream(compressed,CompressionMode.Decompress);
        const int stride=SourceWidth*4;
        var scan=new byte[stride+1]; var pixels=new byte[stride*SourceHeight];
        for(var y=0;y<SourceHeight;y++)
        {
            inflate.ReadExactly(scan);
            for(var x=0;x<stride;x++)
            {
                var i=y*stride+x; var left=x>=4?pixels[i-4]:0; var up=y>0?pixels[i-stride]:0;
                var corner=y>0&&x>=4?pixels[i-stride-4]:0; var p=left+up-corner;
                var pa=Math.Abs(p-left);var pb=Math.Abs(p-up);var pc=Math.Abs(p-corner);
                var filter=scan[0] switch {0=>0,1=>left,2=>up,3=>(left+up)/2,
                    4=>pa<=pb&&pa<=pc?left:pb<=pc?up:corner,_=>throw new InvalidDataException("Unsupported PNG filter.")};
                pixels[i]=unchecked((byte)(scan[x+1]+filter));
            }
        }
        return pixels;
    }
}
