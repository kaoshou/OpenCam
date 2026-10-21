namespace ScreenRecorder.Media.Projects;
public sealed record GifStructureInfo(int Width, int Height, long FrameCount, long DurationCentiseconds, bool InfiniteLoop);
public static class GifStructureReader
{
    public static GifStructureInfo Read(Stream input, CancellationToken ct = default)
    {
        int Byte()
        {
            ct.ThrowIfCancellationRequested();
            var value = input.ReadByte();
            return value < 0 ? throw new InvalidDataException("Truncated GIF.") : value;
        }
        int Word() => Byte() | Byte() << 8;
        void Skip(int count) { for (var i = 0; i < count; i++) Byte(); }
        void Blocks() { int count; while ((count = Byte()) != 0) Skip(count); }
        var header = new string(Enumerable.Range(0, 6).Select(_ => (char)Byte()).ToArray());
        if (header is not ("GIF89a" or "GIF87a")) throw new InvalidDataException("Invalid GIF header.");
        var width = Word(); var height = Word(); var packed = Byte(); Skip(2);
        if (width == 0 || height == 0) throw new InvalidDataException("Invalid GIF dimensions.");
        if ((packed & 128) != 0) Skip(3 * (1 << ((packed & 7) + 1)));
        long frames = 0, duration = 0;
        var loop = false; var delay = 0;
        while (true)
        {
            var marker = Byte();
            if (marker == 0x3b)
            {
                if (frames == 0 || input.ReadByte() != -1) throw new InvalidDataException("Invalid GIF end.");
                return new(width, height, frames, duration, loop);
            }
            if (marker == 0x21)
            {
                var kind = Byte();
                if (kind == 0xf9)
                {
                    if (Byte() != 4) throw new InvalidDataException("Invalid GIF graphics control.");
                    Byte(); delay = Word(); Byte();
                    if (Byte() != 0 || delay == 0) throw new InvalidDataException("Invalid GIF delay.");
                }
                else if (kind == 0xff)
                {
                    var count = Byte();
                    var app = new string(Enumerable.Range(0, count).Select(_ => (char)Byte()).ToArray());
                    if (app == "NETSCAPE2.0")
                    {
                        if (Byte() != 3 || Byte() != 1) throw new InvalidDataException("Invalid GIF loop.");
                        loop = Word() == 0;
                        if (Byte() != 0) throw new InvalidDataException("Invalid GIF loop end.");
                    }
                    else Blocks();
                }
                else Blocks();
            }
            else if (marker == 0x2c)
            {
                var left = Word(); var top = Word(); var w = Word(); var h = Word(); var flags = Byte();
                if (w == 0 || h == 0 || left + w > width || top + h > height || delay == 0)
                    throw new InvalidDataException("Invalid GIF frame.");
                if ((flags & 128) != 0) Skip(3 * (1 << ((flags & 7) + 1)));
                var codeSize = Byte();
                if (codeSize is < 2 or > 8) throw new InvalidDataException("Invalid GIF LZW size.");
                Blocks();
                frames = checked(frames + 1); duration = checked(duration + delay); delay = 0;
            }
            else throw new InvalidDataException("Unexpected GIF block.");
        }
    }
}
