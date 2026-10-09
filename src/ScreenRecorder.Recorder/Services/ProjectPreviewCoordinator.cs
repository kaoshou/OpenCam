// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Media.Projects;
using Serilog;

namespace ScreenRecorder.Recorder.Services;

public delegate Task ProjectPlayback(RecordingProject project, long ticks,
    Func<Guid, CancellationToken, Task<FileStream>> open,
    Func<ProjectPreviewFrame, Task> frame, Action<long> position, CancellationToken ct);
public delegate Task ProjectConfiguredPlayback(RecordingProject project, long ticks, ProjectPreviewSettings settings,
    Func<Guid, CancellationToken, Task<FileStream>> open,
    Func<ProjectPreviewFrame, Task> frame, Action<long> position, CancellationToken ct);

/// <summary>Recorder-owned playback. A short UI lease bounds playback after UI loss.</summary>
public sealed class ProjectPreviewCoordinator : IAsyncDisposable
{
    private readonly ProjectRecordingCoordinator _coordinator;
    private readonly ProjectConfiguredPlayback _play;
    private readonly TimeSpan _leaseDuration;
    private readonly object _sync = new();
    private CancellationTokenSource? _cancel;
    private Task? _worker;
    private ProjectPlaybackState _state = new(Guid.Empty, 0, false, null);
    private ProjectFrameReply? _frame;
    private long _leaseUntil;
    private Exception? _failure;
    private ProjectPreviewSettings? _settings;
    public ProjectPlaybackState State { get { lock (_sync) return _state; } }

    public ProjectPreviewCoordinator(ProjectRecordingCoordinator coordinator, ProjectPlayback play, TimeSpan? leaseDuration = null)
        : this(coordinator, (project, ticks, settings, open, frame, position, ct) => play(project, ticks, open, frame, position, ct), leaseDuration) { }

    public ProjectPreviewCoordinator(ProjectRecordingCoordinator coordinator, ProjectConfiguredPlayback play, TimeSpan? leaseDuration = null)
    {
        (_coordinator, _play, _leaseDuration) = (coordinator, play, leaseDuration ?? TimeSpan.FromSeconds(3));
        coordinator.AttachPreviewStop(StopAsync);
    }

    public async Task<ProjectPlaybackState> PlayAsync(Guid projectId, long revision, long ticks, ProjectPreviewQuality quality = ProjectPreviewQuality.P720, bool muted = false)
    {
        await StopAsync();
        var project = _coordinator.Current;
        if (project?.ProjectId != projectId || project.Revision != revision ||
            _coordinator.Mode is not (ProjectMode.Ready or ProjectMode.Paused) ||
            _coordinator.ExportStatus?.State == RecordingExportState.Running || ProjectTimeline.Build(project).Locate(ticks) is null)
            throw new InvalidOperationException("Preview snapshot or position is unavailable.");
        var generation = Guid.NewGuid();
        var settings = new ProjectPreviewSettings(ProjectPreviewFormat.Resolve(project.Canvas, quality));
        settings.Audio.Muted = muted;
        Log.Information("Project preview starting: generation {Generation}, clips {Clips}, sources with audio {AudioSources}, muted clips {MutedClips}, zero-volume clips {ZeroVolumeClips}",
            generation, project.Clips.Length, project.Sources.Count(s => s.AudioCodec is not null),
            project.Clips.Count(c => c.Muted), project.Clips.Count(c => c.Volume == 0));
        var life = _cancel = new CancellationTokenSource();
        lock (_sync)
        {
            _state = new(generation, ticks, true, null) { Muted = muted };
            _settings = settings;
            _frame = null;
            _leaseUntil = Environment.TickCount64 + (long)_leaseDuration.TotalMilliseconds;
        }
        _worker = Task.Run(async () =>
        {
            var watchdog = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(50, life.Token);
                        lock (_sync) if (Environment.TickCount64 > _leaseUntil) { life.Cancel(); return; }
                    }
                }
                catch (OperationCanceledException) when (life.IsCancellationRequested) { }
            });
            try
            {
                await _play(project, ticks, settings, async (clip, ct) =>
                    (await _coordinator.OpenMediaSourceAsync(projectId, revision, clip, ct)).Stream,
                    frame =>
                    {
                        lock (_sync) if (!life.IsCancellationRequested && _state.Generation == generation)
                            _frame = new(revision, frame.TimelineTicks, frame.ClipId, frame.Rgba)
                                { PixelWidth = settings.Size.Width, PixelHeight = settings.Size.Height };
                        return Task.CompletedTask;
                    }, position =>
                    {
                        lock (_sync) if (!life.IsCancellationRequested && _state.Generation == generation)
                            _state = _state with { TimelineTicks = position };
                    }, life.Token);
            }
            catch (OperationCanceledException) when (life.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "Project preview failed: generation {Generation}", generation);
                lock (_sync)
                {
                    if (ex is ProjectPreviewShutdownException) _failure = ex;
                    _state = _state with { Error = ex.Message };
                }
            }
            finally
            {
                life.Cancel();
                await watchdog;
                lock (_sync) _state = _state with { Playing = false };
            }
        });
        lock (_sync) return _state;
    }

    public (ProjectPlaybackState State, ProjectFrameReply? Frame) Query(Guid generation)
    {
        lock (_sync)
        {
            if (_state.Generation != generation) throw new InvalidOperationException("Obsolete preview generation.");
            _leaseUntil = Environment.TickCount64 + (long)_leaseDuration.TotalMilliseconds;
            return (_state, _frame);
        }
    }

    public ProjectPlaybackState SetMuted(Guid generation, bool muted)
    {
        lock (_sync)
        {
            if (_state.Generation != generation || !_state.Playing || _settings is null)
                throw new InvalidOperationException("Obsolete preview generation.");
            _settings.Audio.Muted = muted;
            _state = _state with { Muted = muted };
            return _state;
        }
    }

    public async Task StopAsync()
    {
        _cancel?.Cancel();
        if (_worker is not null) await _worker;
        _cancel?.Dispose(); _cancel = null; _worker = null;
        // An unconfirmed output shutdown must not allow capture to resume.
        if (_failure is not null) throw new IOException("Preview stopped with an error; restart before recording.", _failure);
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
