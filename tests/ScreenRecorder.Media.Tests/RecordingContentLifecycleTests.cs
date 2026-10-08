// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
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
