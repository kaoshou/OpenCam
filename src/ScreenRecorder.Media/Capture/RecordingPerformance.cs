// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
namespace ScreenRecorder.Media.Capture;

internal sealed record RecordingPerformanceSnapshot(double ElapsedSeconds, long? Frames, double? OutputFps,
    double? FfmpegFps, double? Speed, double? MediaSeconds, long? DuplicatedFrames, long? DroppedFrames,
    long? OutputBytes, double? WriteWaitMs, double? MaxWriteWaitMs);

internal sealed class RecordingPerformance
{
    private readonly object _gate = new();
    private long? _frames, _duplicates, _drops;
    private double? _fps, _speed, _mediaSeconds;
    private long _bytes;
    private double _writeMs, _maxWriteMs;

    public void ReadLine(string line)
    {
        var separator = line.IndexOf('=');
        if (separator < 1) return;
        var key = line[..separator];
        var value = line[(separator + 1)..].Trim();
        lock (_gate)
        {
            if (key is "frame" or "dup_frames" or "drop_frames")
            {
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)) return;
                if (key == "frame") _frames = count;
                else if (key == "dup_frames") _duplicates = count;
                else _drops = count;
            }
            else if (key is "fps" or "speed" or "out_time_us")
            {
                if (key == "speed" && value.EndsWith('x')) value = value[..^1];
                var parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    && double.IsFinite(number) && number >= 0 ? (double?)number : null;
                if (key == "fps") _fps = parsed;
                else if (key == "speed") _speed = parsed;
                else _mediaSeconds = parsed / 1_000_000;
            }
        }
    }

    public void NoteWrite(int bytes, double milliseconds)
    {
        lock (_gate)
        {
            _bytes += bytes;
            _writeMs += milliseconds;
            _maxWriteMs = Math.Max(_maxWriteMs, milliseconds);
        }
    }

    public RecordingPerformanceSnapshot Snapshot(double elapsedSeconds)
    {
        lock (_gate) return new(elapsedSeconds, _frames, elapsedSeconds > 0 && _frames is { } frames ? frames / elapsedSeconds : null,
            _fps, _speed, _mediaSeconds, _duplicates, _drops, _bytes, _writeMs, _maxWriteMs);
    }
}
