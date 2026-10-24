// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.IO.Compression;
namespace ScreenRecorder.Core.Models;

/// <summary>The user's original transparent PNG, decoded once without a new image dependency.</summary>
public static class PresentationPointerImage
{
    public const int Width = 1536, Height = 1024;
    public const int HotspotX = 232, HotspotY = 106;
    public const double Scale = .04;
    private static readonly Lazy<byte[]> Pixels = new(Decode);

    public static uint Sample(double x, double y)
    {
        var sx = (int)Math.Round(HotspotX + x / Scale);
        var sy = (int)Math.Round(HotspotY + y / Scale);
        if (sx < 0 || sy < 0 || sx >= Width || sy >= Height) return 0;
        var p = Pixels.Value;
        var i = (sy * Width + sx) * 4;
        // Ignore barely visible background specks in the supplied PNG when used at cursor size.
        if (p[i + 3] < 32) return 0;
        return (uint)(p[i + 3] << 24 | p[i] << 16 | p[i + 1] << 8 | p[i + 2]);
    }

    private static byte[] Decode()
    {
        using var resource = typeof(PresentationPointerImage).Assembly.GetManifestResourceStream(
            "ScreenRecorder.Core.Assets.PresentationPointer.png") ?? throw new InvalidDataException("Pointer image missing.");
        using var png = new MemoryStream(); resource.CopyTo(png);
        var bytes = png.ToArray();
        using var compressed = new MemoryStream();
        for (var offset = 8; offset + 12 <= bytes.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            if (length < 0 || length > bytes.Length - offset - 12) throw new InvalidDataException("Invalid pointer PNG.");
            var kind = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (kind == "IHDR" && (length != 13 ||
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8, 4)) != Width ||
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12, 4)) != Height ||
                bytes[offset + 16] != 8 || bytes[offset + 17] != 6 || bytes[offset + 20] != 0))
                throw new InvalidDataException("Unsupported pointer PNG format.");
            if (kind == "IDAT") compressed.Write(bytes, offset + 8, length);
            offset += length + 12;
        }
        compressed.Position = 0;
        using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
        const int stride = Width * 4;
        var scan = new byte[stride + 1];
        var pixels = new byte[stride * Height];
        for (var y = 0; y < Height; y++)
        {
            inflate.ReadExactly(scan);
            for (var x = 0; x < stride; x++)
            {
                var i = y * stride + x;
                var left = x >= 4 ? pixels[i - 4] : 0;
                var up = y > 0 ? pixels[i - stride] : 0;
                var corner = y > 0 && x >= 4 ? pixels[i - stride - 4] : 0;
                var p = left + up - corner;
                var pa = Math.Abs(p - left); var pb = Math.Abs(p - up); var pc = Math.Abs(p - corner);
                var filter = scan[0] switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2,
                    4 => pa <= pb && pa <= pc ? left : pb <= pc ? up : corner,
                    _ => throw new InvalidDataException("Unsupported pointer PNG filter.") };
                pixels[i] = unchecked((byte)(scan[x + 1] + filter));
            }
        }
        return pixels;
    }
}
