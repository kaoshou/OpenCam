// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;

namespace ScreenRecorder.ProjectPreviewProbe;

internal sealed record StreamingVideoFrame(long Pts, long TimeNumerator, long TimeDenominator, byte[] Rgba);

/// <summary>Diagnostic streaming decoder, not a selected or activated product player.</summary>
internal static class StreamingVideoDecoder
{
    internal static Task DecodeAsync(string ffmpeg, FileStream source, int width, int height,
        Func<StreamingVideoFrame, CancellationToken, Task> publish, CancellationToken ct)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(width));
        ArgumentNullException.ThrowIfNull(publish);
        var geometry = FormattableString.Invariant($"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,format=rgba,split=2[v][h]");
        // Separate bounded pipes carry actual pixels and their encoder PTS/hash. Neither clock nor frame count is guessed from FPS.
        string[] args = ["-nostdin", "-hide_banner", "-loglevel", "quiet", "-copyts", "-protocol_whitelist", "fd,pipe",
            "-i", "fd:", "-filter_complex", geometry,
            "-map", "[v]", "-an", "-c:v", "rawvideo", "-threads", "1", "-fps_mode", "passthrough",
            "-enc_time_base", "demux", "-f", "rawvideo", "pipe:1",
            "-map", "[h]", "-an", "-c:v", "rawvideo", "-threads", "1", "-fps_mode", "passthrough",
            "-enc_time_base", "demux", "-f", "framehash", "-hash", "sha256", "pipe:2"];
        return MacFileDescriptorProcess.RunStreamingAsync(ffmpeg, args, source, async (pixels, metadata, token, abort) =>
        {
            using var reader = new StreamReader(metadata, leaveOpen: true);
            long numerator = 0, denominator = 0;
            long? previous = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                // Read both concurrently: a raw frame can exceed the OS pipe size before FFmpeg writes its hash row.
                var raw = ReadFrameAsync(pixels, checked(width * height * 4), token);
                var row = ReadRowAsync(reader, token);
                await MacFileDescriptorProcess.JoinReadersAsync(raw, row, abort);
                var bytes = await raw;
                var line = await row;
                if (line is null && bytes is null) break;
                if (line is null || bytes is null) throw new InvalidDataException("Unpaired frame/PTS payload");
                foreach (var header in line.Headers)
                {
                    if (!header.StartsWith("#tb 0:", StringComparison.Ordinal)) continue;
                    var time = header[6..].Trim().Split('/');
                    if (time.Length != 2 || !long.TryParse(time[0], out numerator) || !long.TryParse(time[1], out denominator)
                        || numerator <= 0 || denominator <= 0) throw new InvalidDataException("Invalid frame time base");
                }
                var fields = line.Row.Split(',', StringSplitOptions.TrimEntries);
                if (numerator <= 0 || denominator <= 0 || fields.Length != 6 || fields[0] != "0"
                    || !long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pts)
                    || !int.TryParse(fields[4], out var size) || size != bytes.Length
                    || previous is {} prior && pts <= prior
                    || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(fields[5], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid frame metadata or pixel hash");
                previous = pts;
                token.ThrowIfCancellationRequested();
                await publish(new(pts, numerator, denominator, bytes), token);
            }
            return true;
        }, ct);
    }

    private static async Task<byte[]?> ReadFrameAsync(Stream stream, int size, CancellationToken ct)
    {
        var bytes = new byte[size];
        var count = 0;
        while (count < size)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), ct);
            if (read == 0)
            {
                if (count == 0) return null;
                throw new InvalidDataException("Truncated raw frame");
            }
            count += read;
        }
        return bytes;
    }

    private sealed record RowData(List<string> Headers, string Row);
    private static async Task<RowData?> ReadRowAsync(StreamReader reader, CancellationToken ct)
    {
        var headers = new List<string>();
        // Bound every row before allocating an unbounded ReadLine result, including malformed helper output.
        for (var lines = 0; lines < 32; lines++)
        {
            var chars = new char[4096];
            var count = 0;
            var one = new char[1];
            while (true)
            {
                var read = await reader.ReadAsync(one.AsMemory(), ct);
                if (read == 0)
                {
                    if (count == 0) return null;
                    throw new InvalidDataException("Truncated frame metadata");
                }
                if (one[0] == '\n') break;
                if (count == chars.Length) throw new InvalidDataException("Frame metadata row quota exceeded");
                chars[count++] = one[0];
            }
            var line = new string(chars, 0, count).Trim();
            if (line.StartsWith('#')) { headers.Add(line); continue; }
            return new(headers, line);
        }
        throw new InvalidDataException("Frame metadata header quota exceeded");
    }
}
