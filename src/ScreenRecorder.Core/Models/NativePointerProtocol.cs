// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace ScreenRecorder.Core.Models;

public readonly record struct NativeFrameClock(long SourcePtsMicroseconds, long PresentationMicroseconds,
    long AcquisitionMicroseconds, ulong Sequence);
public readonly record struct NativePointerEvent(PointerSample? Pointer, NativeFrameClock? Frame);

/// <summary>Per-segment parser for owned native producers. Metadata is private media data, not a log message.
/// The caller must stop collecting on any invalid record and mark the track incomplete.</summary>
public sealed class NativePointerProtocol
{
    private readonly int _width, _height;
    private long _lastPointer = -1;
    private NativeFrameClock? _lastFrame;
    private long? _origin;

    public NativePointerProtocol(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(width));
        _width = width; _height = height;
    }

    public NativePointerEvent? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var pointer = line.StartsWith("POINTER", StringComparison.Ordinal);
        var frame = line.StartsWith("FRAME_CLOCK", StringComparison.Ordinal);
        if (!pointer && !frame) return null;
        if (line.Length > 256) throw Invalid();
        var words = line.Split(' ');
        if (words.Length != (pointer ? 7 : 6) || words[1] != "v1") throw Invalid();
        if (pointer)
        {
            var time = Number(words[2]); var x = Number(words[3]); var y = Number(words[4]);
            var visible = Number(words[5]); var buttons = Number(words[6]);
            if (words[0] != "POINTER" || time <= _lastPointer || x >= _width || y >= _height ||
                visible > 1 || buttons > 7 || (visible == 0 && (x != 0 || y != 0 || buttons != 0))) throw Invalid();
            _lastPointer = time;
            return new(new PointerSample(time, x, y, visible == 1, (int)buttons), null);
        }
        var pts = Number(words[2]); var presented = Number(words[3]); var acquired = Number(words[4]);
        var sequence = Number(words[5]);
        if (words[0] != "FRAME_CLOCK" || acquired > presented || sequence == 0 || presented < pts) throw Invalid();
        var origin = presented - pts;
        // VideoToolbox's Matroska transport has millisecond precision. Preserve its actual PTS;
        // permit sub-millisecond quantization only, never a guessed startup/progress offset.
        if (_origin is { } initial && Math.Abs(origin - initial) > 999) throw Invalid();
        if (_lastFrame is { } previous && (pts <= previous.SourcePtsMicroseconds ||
            presented <= previous.PresentationMicroseconds || acquired < previous.AcquisitionMicroseconds ||
            (ulong)sequence < previous.Sequence ||
            ((ulong)sequence == previous.Sequence && acquired != previous.AcquisitionMicroseconds))) throw Invalid();
        var result = new NativeFrameClock(pts, presented, acquired, (ulong)sequence);
        _origin ??= origin; _lastFrame = result;
        return new(null, result);
    }

    private static long Number(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 0
            ? number : throw Invalid();
    private static InvalidDataException Invalid() => new("Invalid native pointer metadata.");
}
