// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using System.Text.Json;

namespace ScreenRecorder.Recorder.Services;

public interface IPreflightLiveSession : IAsyncDisposable
{
    Task StartAsync(RecordingConfiguration snapshot, CancellationToken ct);
    PreflightStatus ReadStatus(DateTimeOffset now);
    ProjectFrameReply? ReadFrame();
    Task StopAsync(CancellationToken ct);
}

public sealed class PreflightCoordinator(RecordingCaptureLease capture, Func<IPreflightLiveSession> create) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Operation? _current;
    private long _revision;
    private sealed class Operation(Guid id, long revision, RecordingConfiguration configuration,
        IPreflightLiveSession session, IAsyncDisposable lease)
    {
        public readonly Guid Id = id;
        public readonly long Revision = revision;
        public readonly RecordingConfiguration Configuration = configuration;
        public readonly IPreflightLiveSession Session = session;
        public readonly IAsyncDisposable Lease = lease;
        public readonly CancellationTokenSource Cancellation = new();
        public volatile PreflightPhase Phase = PreflightPhase.Preparing;
    }
    public Guid? CurrentOperation => Volatile.Read(ref _current)?.Id;
    public async Task<PreflightStatus> BeginAsync(Guid operation, RecordingConfiguration configuration, CancellationToken ct)
    {
        var frozen = JsonSerializer.Deserialize<RecordingConfiguration>(JsonSerializer.Serialize(configuration))!;
        await _gate.WaitAsync(ct);
        try
        {
            if (_current is not null) throw new InvalidOperationException("A preflight is already open.");
            var lease = await capture.AcquireAsync(operation, ct);
            IPreflightLiveSession session;
            try { session = create(); }
            catch { await lease.DisposeAsync(); throw; }
            var current = new Operation(operation, Interlocked.Increment(ref _revision), frozen, session, lease);
            Volatile.Write(ref _current, current);
            try
            {
                using var start = CancellationTokenSource.CreateLinkedTokenSource(ct, current.Cancellation.Token);
                start.CancelAfter(TimeSpan.FromSeconds(40));
                await session.StartAsync(frozen, start.Token);
                current.Phase = PreflightPhase.Checking;
                return ReadStatus(operation, current.Revision);
            }
            catch
            {
                current.Phase = PreflightPhase.Failed;
                // Keep ownership when shutdown cannot be confirmed.
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await StopCoreAsync(current, stop.Token);
                throw;
            }
        }
        finally { _gate.Release(); }
    }
    private Operation Require(Guid operation, long? revision = null)
    {
        var current = Volatile.Read(ref _current);
        if (current is null || current.Id != operation || (revision is { } r && current.Revision != r))
            throw new InvalidOperationException("Preflight identity changed.");
        return current;
    }
    public PreflightStatus ReadStatus(Guid operation, long revision)
    {
        var current = Require(operation, revision);
        return current.Session.ReadStatus(DateTimeOffset.UtcNow) with
        { OperationId = operation, ConfigurationRevision = current.Revision, Phase = current.Phase };
    }
    public ProjectFrameReply? ReadFrame(Guid operation, long revision)
    {
        var current = Require(operation, revision);
        return current.Session.ReadFrame() is { } frame ? frame with { ClipId = operation, Revision = revision } : null;
    }
    public PreflightStatus ReadCurrentStatus()
    {
        var current = Volatile.Read(ref _current);
        return current is null ? PreflightStatus.Empty : ReadStatus(current.Id, current.Revision);
    }
    public async Task EndAsync(Guid operation, CancellationToken ct)
    {
        var current = Volatile.Read(ref _current);
        if (current is null) return;
        if (current.Id != operation) throw new InvalidOperationException("Preflight identity changed.");
        current.Phase = PreflightPhase.Stopping;
        current.Cancellation.Cancel();
        await _gate.WaitAsync(ct);
        try { if (ReferenceEquals(current, _current)) await StopCoreAsync(current, ct); }
        finally { _gate.Release(); }
    }
    private async Task StopCoreAsync(Operation current, CancellationToken ct)
    {
        await current.Session.StopAsync(ct);
        await current.Session.DisposeAsync();
        await current.Lease.DisposeAsync();
        Volatile.Write(ref _current, null);
        current.Cancellation.Dispose();
    }
    public async ValueTask DisposeAsync()
    {
        if (CurrentOperation is { } operation)
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await EndAsync(operation, stop.Token);
        }
    }
}
