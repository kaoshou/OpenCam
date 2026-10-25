// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task EmptyStartupFailureReturnsReadyAndCanRetryWithoutExporting()
    {
        await using var scope = new RecordingScope();
        scope.Configuration.EncoderType = ScreenRecorder.Core.Enums.HardwareEncoderType.SoftwareCpu;
        scope.Factory.EmptyOutput = true;
        scope.Factory.StartFailure = new IOException("capture_start_timeout; source_wait");
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var failed = await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid());
        Assert.False(failed.Success);
        Assert.Empty(project.Current!.Clips);
        Assert.Equal(ProjectMode.Ready, project.Mode);
        scope.Factory.EmptyOutput = false;
        scope.Factory.StartFailure = null;
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        Assert.Single(project.Current.Clips);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupWithBytesOrUnconfirmedCleanupKeepsRecoveryState(bool cleanupFails)
    {
        await using var scope = new RecordingScope();
        scope.Factory.EmptyOutput = cleanupFails;
        scope.Factory.FailCleanup = cleanupFails;
        scope.Factory.StartFailure = new IOException("source_wait");
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.False((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Equal(ProjectMode.SaveFailed, project.Mode);
        Assert.NotNull(scope.Recorder.CurrentSession);
        Assert.Empty(project.Current!.Clips);
        Assert.All(scope.Factory.Paths, path => Assert.True(File.Exists(path)));
        Assert.False((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        scope.Factory.FailCleanup = false;
        await scope.Recorder.DisposeAsync(); // Retained engine must close before the project's bound handle.
    }

    [Fact]
    public async Task FailedResumePreservesSavedClipAndCanFinish()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid(), autoExportOnStop: false)).Success);
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        var saved = project.Current!;
        var bytes = await File.ReadAllBytesAsync(scope.Factory.Paths[0]);
        scope.Factory.StartFailure = new IOException("source_wait");
        Assert.False((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Equal(ProjectMode.SaveFailed, project.Mode);
        Assert.Equal(saved.Sources, project.Current!.Sources);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(scope.Factory.Paths[0]));
        scope.Factory.StartFailure = null;
        Assert.True((await project.FinishAndExportAsync(Guid.NewGuid())).Success);
        Assert.Single(project.Current.Clips);
    }

    [Fact]
    public async Task RecordingContentLifecycleTests_StartNewContentNeedsNoNameOrManifestPath()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var destination = scope.Configuration.OutputDirectory;
        scope.Factory.BeforeStart = () =>
        {
            Assert.True(File.Exists(Path.Combine(project.ProjectDirectory!, "project.opencam")));
            Assert.NotEqual(Guid.Empty, project.Current!.ProjectId);
        };
        var started = await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid());
        Assert.True(started.Success, started.ErrorCode);
        Assert.Equal("OpenCam Recordings", new DirectoryInfo(project.ProjectDirectory!).Parent!.Name);
        Assert.False(string.IsNullOrWhiteSpace(project.Current!.Name));
        Assert.Equal(destination, project.OutputDirectory);
        Assert.Equal(destination, scope.Configuration.OutputDirectory);
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        Assert.Single(project.Current.Sources);
    }

    [Fact]
    public async Task RecordingContentLifecycleTests_SameTimestampCreatesDistinctContent()
    {
        await using var scope = new RecordingScope();
        var factory = new RecordingContentFactory(new JsonProjectStore(), new FixedContentClock());
        await using var first = await factory.CreateAsync(scope.Configuration.OutputDirectory);
        await using var second = await factory.CreateAsync(scope.Configuration.OutputDirectory);
        Assert.NotEqual(first.ProjectDirectory, second.ProjectDirectory);
        Assert.NotEqual(first.Current.ProjectId, second.Current.ProjectId);
        Assert.Contains("2026-10-09", first.Current.Name);
        Assert.True(File.Exists(Path.Combine(first.ProjectDirectory, "project.opencam")));
        Assert.True(File.Exists(Path.Combine(second.ProjectDirectory, "project.opencam")));
    }

    [Fact]
    public async Task RecordingContentLifecycleTests_ReplayStartDoesNotCreateOrCaptureTwice()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var operation = Guid.NewGuid();
        var first = await project.StartNewContentAsync(scope.Configuration, operation);
        Assert.True(first.Success, first.ErrorCode);
        Assert.Equal(first, await project.StartNewContentAsync(scope.Configuration, operation));
        scope.Configuration.Fps = 60;
        Assert.False((await project.StartNewContentAsync(scope.Configuration, operation)).Success);
        Assert.Single(scope.Factory.Paths);
        Assert.Single(Directory.GetDirectories(Path.Combine(scope.Configuration.OutputDirectory, "OpenCam Recordings")));
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
    }

    [Fact]
    public async Task RecordingContentLifecycleTests_CreateFailureDoesNotStartCapture()
    {
        await using var scope = new RecordingScope();
        File.WriteAllText(Path.Combine(scope.Configuration.OutputDirectory, "OpenCam Recordings"), "occupied");
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.False((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Empty(scope.Factory.Paths);
        Assert.Null(scope.Recorder.CurrentSession);
        Assert.Null(project.Current);
    }

    [Fact]
    public async Task RecordingContentLifecycleTests_NewContentRejectsPausedOwnerAndPreservesSources()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        var id = project.Current!.ProjectId;
        Assert.False((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Equal(id, project.Current.ProjectId);
        Assert.Single(project.Current.Sources);
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        Assert.True((await project.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.NotEqual(id, project.Current.ProjectId);
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        Assert.All(scope.Factory.Paths, path => Assert.True(File.Exists(path)));
    }

    private sealed class FixedContentClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
