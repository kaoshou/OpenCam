// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using System.Collections.Immutable;
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Recorder.Services;

internal sealed record ProjectMediaLease(FileStream Stream, ProjectSource Source, ProjectClip Clip) : IDisposable
{
    public void Dispose() => Stream.Dispose();
}

/// <summary>One bounded background decoder. Query never waits for media decoding under the command gate.</summary>
public sealed class ProjectWaveformService(ProjectRecordingCoordinator coordinator, IProjectMediaProcess process) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(Guid Project, long Revision, Guid Clip), ProjectWaveformReply> _cache = new();
    private (Guid Project, long Revision, Guid Clip) _key;
    private Task<ProjectWaveformReply>? _running;
    private CancellationTokenSource? _cancel;
    private bool _disposed;
    private bool _superseded;

    public async Task<ProjectWaveformReply> QueryAsync(Guid projectId, long revision, Guid clipId)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ProjectWaveformService));
            if (coordinator.Current?.ProjectId != projectId || coordinator.Current.Revision != revision ||
                coordinator.Mode is not (ProjectMode.Ready or ProjectMode.Paused))
                throw new InvalidOperationException("Waveform snapshot is no longer current.");
            var key = (projectId, revision, clipId);
            if (_cache.TryGetValue(key, out var cached)) return cached;
            if (_running is not null)
            {
                if (!_running.IsCompleted)
                {
                    if (_key != key) { _superseded = true; _cancel!.Cancel(); }
                    return new(clipId, revision, null);
                }
                var finished = await _running;
                if (!_superseded) _cache[_key] = finished;
                while (_cache.Count > 64) _cache.Remove(_cache.Keys.First());
                _cancel!.Dispose();
                _cancel = null;
                _running = null;
                if (_key == key && !_superseded) return finished;
            }
            var selected = coordinator.Current.Clips.Single(c => c.Id == clipId);
            var parts = ProjectClipProjection.Leaves(selected).ToArray();
            _key = key;
            _superseded = false;
            _cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var token = _cancel.Token;
            _running = Task.Run(async () =>
            {
                    try
                    {
                        var decoded = new List<ProjectWaveformResult>();
                        foreach (var part in parts)
                        {
                        using var lease = await coordinator.OpenMediaSourceAsync(projectId, revision, part.Id, token);
                        if (lease.Stream.Length != lease.Source.FileSize ||
                            !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(lease.Stream, token)),
                                lease.Source.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Original source fingerprint changed.");
                        lease.Stream.Position = 0;
                        var tb = lease.Source.Timing.TimeBase;
                        var n = ((BigInteger)lease.Clip.OutPts - lease.Clip.InPts) * tb.Numerator * 48000;
                        var samples = (n + tb.Denominator - 1) / tb.Denominator;
                        var buckets = (int)BigInteger.Min(256, samples);
                        var data = await new ProjectWaveformReader(process).ReadAsync(lease.Stream,
                            lease.Clip.InPts, lease.Clip.OutPts, tb, lease.Source.AudioCodec is not null, buckets, token);
                        decoded.Add(data);
                        }
                        return new ProjectWaveformReply(clipId, revision, Combine(decoded));
                    }
                    catch (OperationCanceledException) { return new(clipId, revision, null, "Waveform loading canceled."); }
                    catch (Exception) { return new(clipId, revision, null, "Waveform could not be decoded. Original media is unchanged."); }
            });
            return new(clipId, revision, null);
        }
        finally { _gate.Release(); }
    }

    internal static ProjectWaveformResult Combine(IReadOnlyList<ProjectWaveformResult> parts)
    {
        if (parts.Count == 1) return parts[0];
        var samples = parts.Sum(p => p.SampleCount);
        var count = (int)Math.Min(256, samples);
        var minima = new float[count]; var maxima = new float[count];
        var energy = new double[count]; var weights = new double[count];
        long offset = 0;
        foreach (var part in parts)
        {
            for (var i = 0; i < part.Buckets.Length; i++)
            {
                var start = offset + (double)i * part.SampleCount / part.Buckets.Length;
                var end = offset + (double)(i + 1) * part.SampleCount / part.Buckets.Length;
                var bucket = part.Buckets[i];
                for (var j = Math.Max(0, (int)(start * count / samples)); j < count && (double)j * samples / count < end; j++)
                {
                    var weight = Math.Min(end, (double)(j + 1) * samples / count) - Math.Max(start, (double)j * samples / count);
                    if (weight <= 0) continue;
                    minima[j] = Math.Min(minima[j], bucket.Minimum); maxima[j] = Math.Max(maxima[j], bucket.Maximum);
                    energy[j] += bucket.Rms * bucket.Rms * weight; weights[j] += weight;
                }
            }
            offset += part.SampleCount;
        }
        return new(parts.Any(p => p.HasAudio), parts.All(p => p.IsSilent), samples,
            Enumerable.Range(0, count).Select(i => new ProjectWaveformBucket(minima[i], maxima[i],
                weights[i] > 0 ? (float)Math.Sqrt(energy[i] / weights[i]) : 0)).ToImmutableArray());
    }

    public async Task SuspendAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _cancel?.Cancel();
            if (_running is not null) await _running;
            _cancel?.Dispose();
            _cancel = null;
            _running = null;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _disposed = true;
            _cancel?.Cancel();
            if (_running is not null) await _running;
            _cancel?.Dispose();
            _cache.Clear();
        }
        finally { _gate.Release(); }
    }
}
