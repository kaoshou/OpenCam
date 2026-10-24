// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Models;

/// <summary>Checks every decoded presentation timestamp after passthrough-rate encoding/muxing.
/// The caller pairs full presentation-order sequences, not packet DTS or progress reports.</summary>
public sealed class PointerClockVerifier
{
    private long _native = -1, _output = long.MinValue, _count;
    private long? _offset;
    private bool _failed, _finished;

    public void Add(long nativePtsMicroseconds, long outputPtsMicroseconds)
    {
        if (_failed || _finished) throw new InvalidDataException("Pointer clock verifier is closed.");
        try
        {
            var offset = checked(outputPtsMicroseconds - nativePtsMicroseconds);
            if (nativePtsMicroseconds < 0 || nativePtsMicroseconds <= _native || outputPtsMicroseconds <= _output ||
                offset is < -60_000_000 or > 60_000_000 ||
                (_offset is { } initial && Math.Abs(offset - initial) > 999))
                throw new InvalidDataException("Video frames no longer match pointer clock.");
            _offset ??= offset; _native = nativePtsMicroseconds; _output = outputPtsMicroseconds; _count++;
        }
        catch { _failed = true; throw; }
    }

    public long Finish(long nativeFrameCount, long outputFrameCount)
    {
        if (_failed || _finished || _count == 0 || nativeFrameCount != _count || outputFrameCount != _count || _offset == null)
        {
            _failed = true;
            throw new InvalidDataException("Incomplete video/pointer clock correspondence.");
        }
        _finished = true; return _offset.Value;
    }
}
