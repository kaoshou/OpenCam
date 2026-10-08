// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Numerics;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.ProjectPreviewProbe;

/// <summary>Test-only seek candidate. Intentionally not a complete player or selected product backend.</summary>
internal sealed class FfmpegPipeCandidate(string ffmpeg) : IPreviewCandidate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _latest;
    private Stream? _source;

    public Task OpenAsync(Stream source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!source.CanRead || !source.CanSeek || source.CanWrite) throw new InvalidDataException("Require a read-only seekable source.");
        if (_source is not null) throw new InvalidOperationException("Source is already open.");
        _source = source;
        return Task.CompletedTask;
    }

    public async Task<PreviewProbeFrame> SeekAsync(long sourcePts, ProjectRational timeBase, CancellationToken ct)
    {
        using var own = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_sync) { _latest?.Cancel(); _latest = own; }
        var entered = false;
        try
        {
            await _gate.WaitAsync(own.Token);
            entered = true;
            var source = _source ?? throw new InvalidOperationException("No open source.");
            source.Position = 0;
            var output = await BoundedProcess.RunAsync(ffmpeg, [
                "-hide_banner", "-loglevel", "error", "-copyts",
                "-protocol_whitelist", "pipe", "-i", "pipe:0", "-map", "0:v:0",
                "-vf", "select=gte(pts\\," + sourcePts.ToString(CultureInfo.InvariantCulture) + ")",
                "-frames:v", "1", "-fps_mode", "passthrough", "-enc_time_base", "demux",
                "-c:v", "rawvideo", "-pix_fmt", "yuv420p", "-f", "framehash", "-hash", "sha256", "pipe:1"
            ], source, own.Token);
            own.Token.ThrowIfCancellationRequested();
            return Parse(output, sourcePts, timeBase);
        }
        finally
        {
            if (entered) _gate.Release();
            lock (_sync) { if (ReferenceEquals(_latest, own)) _latest = null; }
        }
    }

    internal static PreviewProbeFrame Parse(string text, long requested, ProjectRational sourceBase)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var parts = lines.Single(l => l.StartsWith("#tb 0:")).Split(':')[1].Trim().Split('/');
        var timeNum = long.Parse(parts[0], CultureInfo.InvariantCulture);
        var timeDen = long.Parse(parts[1], CultureInfo.InvariantCulture);
        var row = lines.First(l => !l.StartsWith('#')).Split(',', StringSplitOptions.TrimEntries);
        var pts = long.Parse(row[2], CultureInfo.InvariantCulture);
        var scaled = (BigInteger)pts * timeNum * sourceBase.Denominator / (timeDen * (BigInteger)sourceBase.Numerator);
        return new(requested, checked((long)scaled), row[5]);
    }

    public Task PlayAsync(CancellationToken ct)
        => throw new NotSupportedException("Probe has no PCM sink or A/V clock. Playback is NOT_IMPLEMENTED.");

    public async Task StopAsync(CancellationToken ct)
    {
        lock (_sync) _latest?.Cancel();
        await _gate.WaitAsync(ct);
        _gate.Release();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _gate.Dispose();
        // Source stream belongs to probe caller, not the decoder.
    }
}
