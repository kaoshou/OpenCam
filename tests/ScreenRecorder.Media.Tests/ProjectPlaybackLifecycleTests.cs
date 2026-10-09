// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task ProjectPlayback_UnconfirmedStopBlocksCaptureButAllowsSave()
    {
        await using var scope = new RecordingScope();
        await using var store = new PlaybackLeaseFixture();
        var coordinator = new ProjectRecordingCoordinator(scope.Recorder, store, new ProjectProbeStub());
        await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "Stop failure");
        await coordinator.StartAsync(scope.Configuration, Guid.NewGuid());
        await coordinator.PauseAsync(Guid.NewGuid());
        var preview = new ProjectPreviewCoordinator(coordinator, (_, _, _, _, _, _) =>
            throw new ProjectPreviewShutdownException(new IOException("Device did not exit")));
        try
        {
            await preview.PlayAsync(coordinator.Current!.ProjectId, coordinator.Current.Revision, 0);
            await UntilAsync(() => !preview.State.Playing);
            Assert.True((await coordinator.SaveAsync(coordinator.Current.Revision)).Success);
            Assert.False((await coordinator.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
            Assert.Equal(ProjectMode.Paused, coordinator.Mode);
            await Assert.ThrowsAsync<IOException>(() => preview.StopAsync());
            await Assert.ThrowsAsync<IOException>(() => coordinator.DisposeAsync().AsTask());
            // Fail-closed ownership is intentional, including on Unix where an
            // open file can otherwise be unlinked during fixture teardown.
            await Assert.ThrowsAnyAsync<IOException>(() => new JsonProjectStore().OpenAsync(
                Path.Combine(coordinator.ProjectDirectory!, "project.opencam")));
        }
        finally
        {
            // The injected playback has no device/process. After testing the
            // failure latch, release this fixture's real project lease. Do not
            // weaken production DisposeAsync or permit capture after failure.
            await store.DisposeAsync();
        }
    }

    private sealed class PlaybackLeaseFixture : IProjectStore, IAsyncDisposable
    {
        private readonly JsonProjectStore _store = new();
        private readonly List<IProjectHandle> _handles = [];
        public async Task<IProjectHandle> CreateAsync(string directory, string name, CancellationToken ct = default)
        {
            var handle = await _store.CreateAsync(directory, name, ct);
            _handles.Add(handle);
            return handle;
        }
        public async Task<IProjectHandle> OpenAsync(string path, CancellationToken ct = default)
        {
            var handle = await _store.OpenAsync(path, ct);
            _handles.Add(handle);
            return handle;
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var handle in _handles) await handle.DisposeAsync();
            _handles.Clear();
        }
    }
    [Fact]
    public async Task ProjectPlayback_DecodeErrorDoesNotPreventSaveOrClose()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "Decode error");
        await coordinator.StartAsync(scope.Configuration, Guid.NewGuid());
        await coordinator.FinishAsync(Guid.NewGuid());
        var preview = new ProjectPreviewCoordinator(coordinator, (_, _, _, _, _, _) => throw new IOException("Bad decoder"));
        await preview.PlayAsync(coordinator.Current!.ProjectId, coordinator.Current.Revision, 0);
        await UntilAsync(() => !preview.State.Playing);
        Assert.Contains("Bad decoder", preview.State.Error);
        var saved = await coordinator.SaveAsync(coordinator.Current.Revision);
        Assert.True(saved.Success, saved.ErrorCode);
        Assert.True((await coordinator.CloseAsync()).Success);
        await preview.DisposeAsync();
    }
    [Fact]
    public async Task ProjectPlayback_SeekJoinsOldGenerationAndCaptureStopsPlayback()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "Preview");
        await coordinator.StartAsync(scope.Configuration, Guid.NewGuid());
        await coordinator.PauseAsync(Guid.NewGuid());
        var stopped = 0;
        await using var preview = new ProjectPreviewCoordinator(coordinator, async (project, ticks, open, frame, position, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { Interlocked.Increment(ref stopped); }
        });
        var project = coordinator.Current!;
        var first = await preview.PlayAsync(project.ProjectId, project.Revision, 0);
        var second = await preview.PlayAsync(project.ProjectId, project.Revision, 1);
        Assert.NotEqual(first.Generation, second.Generation);
        Assert.Equal(1, stopped);
        Assert.Throws<InvalidOperationException>(() => preview.Query(first.Generation));
        Assert.Throws<InvalidOperationException>(() => preview.SetMuted(first.Generation, true));
        Assert.True(preview.SetMuted(second.Generation, true).Muted);
        Assert.False(preview.SetMuted(second.Generation, false).Muted);
        Assert.False(coordinator.Current!.Clips[0].Muted);
        var result = await coordinator.StartAsync(scope.Configuration, Guid.NewGuid());
        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(2, stopped);
        Assert.False(preview.Query(second.Generation).State.Playing);
        await coordinator.FinishAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task ProjectPlayback_LostUiLeaseStopsOutput()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "Lease");
        await coordinator.StartAsync(scope.Configuration, Guid.NewGuid());
        await coordinator.PauseAsync(Guid.NewGuid());
        var stopped = new TaskCompletionSource();
        await using var preview = new ProjectPreviewCoordinator(coordinator, async (project, ticks, open, frame, position, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { stopped.SetResult(); }
        }, TimeSpan.FromMilliseconds(100));
        await preview.PlayAsync(coordinator.Current!.ProjectId, coordinator.Current.Revision, 0);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await coordinator.FinishAsync(Guid.NewGuid());
    }
}
