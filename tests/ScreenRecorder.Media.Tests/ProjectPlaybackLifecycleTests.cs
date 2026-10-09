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
        var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "Stop failure");
        await coordinator.StartAsync(scope.Configuration, Guid.NewGuid());
        await coordinator.PauseAsync(Guid.NewGuid());
        var preview = new ProjectPreviewCoordinator(coordinator, (_, _, _, _, _, _) =>
            throw new ProjectPreviewShutdownException(new IOException("Device did not exit")));
        await preview.PlayAsync(coordinator.Current!.ProjectId, coordinator.Current.Revision, 0);
        await UntilAsync(() => !preview.State.Playing);
        Assert.True((await coordinator.SaveAsync(coordinator.Current.Revision)).Success);
        Assert.False((await coordinator.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Equal(ProjectMode.Paused, coordinator.Mode);
        await Assert.ThrowsAsync<IOException>(() => preview.StopAsync());
        await Assert.ThrowsAsync<IOException>(() => coordinator.DisposeAsync().AsTask());
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
