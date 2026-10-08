// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectRecording_FailedStartupRetainsOwnership_UntilEngineCleanupAndFinish(bool cleanupFailure)
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        scope.Store.FailNextCommittedSave = !cleanupFailure;
        scope.Factory.FailNext = scope.Factory.FailCleanup = cleanupFailure;
        Assert.False((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.False((await project.CloseAsync()).Success);
        Assert.Equal(ProjectMode.SaveFailed, project.Mode);
        scope.Factory.FailCleanup = false;
        var finish = await project.FinishAsync(Guid.NewGuid());
        Assert.True(finish.Success, finish.ErrorCode);
        Assert.Equal(cleanupFailure ? 0 : 1, project.Current!.Sources.Length);
        Assert.True((await project.CloseAsync()).Success);
        Assert.All(scope.Factory.Paths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task ProjectRecording_LinkedSessions_DoesNotCreateAnythingOutsideProject()
    {
        if (OperatingSystem.IsWindows()) return; // Junction/reparse coverage remains in Windows acceptance.
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        var sessions = Path.Combine(project.ProjectDirectory!, "sessions");
        var outside = Directory.CreateDirectory(Path.Combine(scope.Configuration.OutputDirectory, "outside")).FullName;
        Directory.Delete(sessions);
        Directory.CreateSymbolicLink(sessions, outside);
        Assert.False((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Empty(Directory.GetFileSystemEntries(outside));
        Assert.Empty(scope.Factory.Paths);
    }

    [Fact]
    public async Task ProjectRecording_MovedWhilePaused_ContinuesInPinnedProject()
    {
        if (OperatingSystem.IsWindows()) return; // Windows pins directories against rename.
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        var previous = project.ProjectDirectory!;
        var moved = previous + "-moved";
        Directory.Move(previous, moved);
        Directory.CreateDirectory(previous);
        var resumed = await project.StartAsync(scope.Configuration, Guid.NewGuid());
        Assert.True(resumed.Success, resumed.ErrorCode);
        var finish = await project.FinishAsync(Guid.NewGuid());
        Assert.True(finish.Success, finish.ErrorCode);
        Assert.Equal(2, project.Current!.Sources.Length);
        Assert.Empty(Directory.GetFileSystemEntries(previous));
        Assert.All(project.Current.Sources, s => Assert.True(File.Exists(Path.Combine(moved, s.RelativePath))));
    }

    [Theory]
    [InlineData("probe")]
    [InlineData("session")]
    [InlineData("cleanup")]
    [InlineData("stop")]
    public async Task ProjectRecording_FailedFinish_CanRetryWithoutRestartOrRemux(string fault)
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        scope.Probe.FailNext = fault == "probe";
        scope.Store.FailNextCommittedSave = fault == "session";
        scope.Factory.FailCleanup = fault == "cleanup";
        scope.Factory.FailStopNext = fault == "stop";
        Assert.False((await project.FinishAsync(Guid.NewGuid())).Success);
        scope.Factory.FailCleanup = false;
        var retry = await project.FinishAsync(Guid.NewGuid());
        Assert.True(retry.Success, retry.ErrorCode);
        Assert.Single(project.Current!.Sources);
        Assert.All(scope.Factory.Created, engine => Assert.False(engine.IsRunning));
        Assert.True((await project.CloseAsync()).Success);
        Assert.Empty(scope.Remuxer.Inputs);
    }

    [Fact]
    public async Task ProjectRecording_DisplayIdentityChangesWhilePaused_RejectsWrongMonitorResume()
    {
        var display = new MutableProjectDisplay();
        await using var scope = new RecordingScope(display);
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        display.Name = "Another monitor at same index";
        Assert.False((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Single(scope.Factory.Paths);
        Assert.Equal(RecordingState.Paused, scope.Recorder.CurrentState);
    }

    [Theory]
    [InlineData("audio")]
    [InlineData("watchdog")]
    public async Task ProjectRecording_AutomaticStop_DoesNotRemuxOrDelete(string reason)
    {
        await using var scope = new RecordingScope();
        scope.Factory.UseModern = reason == "watchdog";
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        if (reason == "audio")
        {
            scope.Factory.FailNext = true;
            scope.Factory.Created[0].LoseAudio();
        }
        else scope.Health.Healthy = false;
        await UntilAsync(() => scope.Recorder.CurrentState == RecordingState.Completed);
        var dispatcher = new ProjectIpcDispatcher(project);
        var response = await dispatcher.DispatchAsync(new() { MessageType = "GetProjectStatus", PayloadJson = "{}" });
        var status = System.Text.Json.JsonSerializer.Deserialize<ScreenRecorder.Infrastructure.IPC.ProjectReply>(response.DataJson!)!;
        Assert.Equal(ProjectMode.Ready, status.State.Mode);
        Assert.NotNull(status.State.LastError);
        Assert.NotEmpty(project.Current!.Sources);
        Assert.Empty(scope.Remuxer.Inputs);
        Assert.True(File.Exists(scope.Factory.Paths[0]));
    }

    private sealed class MutableProjectDisplay : IDisplayService
    {
        public string Name = "Initial monitor";
        public IReadOnlyList<MonitorInfo> GetMonitors() => [new(0, Name, new(0, 0, 320, 240), true, 1)];
        public MonitorInfo? GetPrimaryMonitor() => GetMonitors()[0];
        public CaptureRegion GetVirtualScreenBounds() => new(0, 0, 320, 240);
    }

    [Fact]
    public async Task ProjectRecording_ThreeSegments_ReopenAppend_NoRemuxOrSourceDeletion()
    {
        await using var scope = new RecordingScope();
        scope.Configuration.DeleteWorkingFileAfterSuccessfulRemux = true;
        var probe = new ProjectProbeStub();
        string manifest;
        string[] hashes;
        await using (var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), probe))
        {
            Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
            manifest = Path.Combine(project.ProjectDirectory!, "project.opencam");
            for (var i = 0; i < 3; i++)
            {
                var operation = Guid.NewGuid();
                Assert.True((await project.StartAsync(scope.Configuration, operation)).Success);
                Assert.True((await project.StartAsync(scope.Configuration, operation)).Success);
                Assert.Equal(i, project.Current!.Sources.Length); // Live MKV is not editable.
                Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
                Assert.Equal(i + 1, project.Current.Sources.Length);
            }
            Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
            hashes = project.Current!.Sources.Select(s => s.Sha256).ToArray();
            Assert.True((await project.CloseAsync()).Success);
        }
        await using (var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), probe))
        {
            Assert.True((await project.OpenAsync(manifest)).Success);
            Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
            Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
            Assert.Equal(4, project.Current!.Sources.Length);
            Assert.Equal(hashes, project.Current.Sources.Take(3).Select(s => s.Sha256));
            Assert.Equal(2, project.Current.Sessions.Length);
            Assert.All(scope.Factory.Paths, path => Assert.True(File.Exists(path)));
        }
        Assert.Empty(scope.Remuxer.Inputs);
        Assert.True(scope.Configuration.DeleteWorkingFileAfterSuccessfulRemux);
        Assert.Empty(Directory.GetFiles(scope.Configuration.OutputDirectory, "*.mp4", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("probe")]
    [InlineData("engine")]
    [InlineData("session")]
    [InlineData("empty")]
    [InlineData("manifest")]
    public async Task ProjectRecording_PauseFailure_BlocksResumeAndPreservesSource(string fault)
    {
        await using var scope = new RecordingScope();
        var probe = new ProjectProbeStub();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), probe);
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        scope.Factory.EmptyOutput = fault == "empty";
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        probe.Fail = fault == "probe";
        scope.Factory.FailCleanup = fault == "engine";
        scope.Store.FailNextCommittedSave = fault == "session";
        if (fault == "manifest") Directory.CreateDirectory(Path.Combine(project.ProjectDirectory!, "project.opencam.bak"));
        Assert.False((await project.PauseAsync(Guid.NewGuid())).Success);
        Assert.Equal(ProjectMode.SaveFailed, project.Mode);
        Assert.False((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Empty(project.Current!.Sources);
        Assert.All(scope.Factory.Paths, path => Assert.True(File.Exists(path)));
        scope.Factory.FailCleanup = false;
    }

    [Fact]
    public async Task ProjectRecording_InternalStop_KeepsSourcesDespiteDeletePreference()
    {
        await using var scope = new RecordingScope();
        scope.Configuration.DeleteWorkingFileAfterSuccessfulRemux = true;
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        var session = scope.Recorder.CurrentSession!;
        Assert.NotNull(session.ProjectId);
        Assert.Equal(ProjectCompletionPolicy.KeepProjectSources, session.CompletionPolicy);
        var result = await scope.Recorder.StopRecordingAsync("parent exit");
        Assert.True(result.Success);
        Assert.Null(result.FinalFilePath);
        Assert.Empty(scope.Remuxer.Inputs);
        Assert.All(scope.Factory.Paths, path => Assert.True(File.Exists(path)));
    }

    private sealed class ProjectProbeStub : IProjectSourceProbe
    {
        public bool Fail { get; set; }
        public int FailAt { get; set; }
        private int _calls;
        public Task<ProjectMediaInfo> ProbeAsync(Stream source, CancellationToken ct = default)
        {
            if (Fail || ++_calls == FailAt) throw new InvalidDataException("Injected probe failure");
            return Task.FromResult(new ProjectMediaInfo(new(new(1, 1000), 0, 1000), 320, 240, "h264", "aac"));
        }
    }
}
