// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
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
            var lease = await coordinator.OpenMediaSourceAsync(projectId, revision, clipId, default);
            _key = key;
            _superseded = false;
            _cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var token = _cancel.Token;
            _running = Task.Run(async () =>
            {
                using (lease)
                {
                    try
                    {
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
                        return new ProjectWaveformReply(clipId, revision, data);
                    }
                    catch (OperationCanceledException) { return new(clipId, revision, null, "Waveform loading canceled."); }
                    catch (Exception) { return new(clipId, revision, null, "Waveform could not be decoded. Original media is unchanged."); }
                }
            });
            return new(clipId, revision, null);
        }
        finally { _gate.Release(); }
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
