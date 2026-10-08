// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData("return", true)]
    [InlineData("outside", true)]
    [InlineData("outside", false)]
    [InlineData("fit", true)]
    [InlineData("zoom", true)]
    public async Task ProjectIpc_CanceledTimelineGestureDoesNotSave(string cancellation, bool trim)
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "Cancel gestures");
        for (var i = 0; i < 2; i++) { await vm.StartAsync(scope.Configuration); await vm.PauseAsync(); }
        var timeline = new ScreenRecorder.UI.Projects.Editor.ProjectTimelineControl { DataContext = vm };
        var window = new Avalonia.Controls.Window { Content = timeline };
        window.Show(); window.UpdateLayout();
        try
        {
            var revision = coordinator.Current!.Revision;
            var initial = new Avalonia.Point(trim ? 18 : 200,82);
            var target = new Avalonia.Point(trim ? 200 : timeline.Bounds.Width-20,82);
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, initial, Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, target);
            switch (cancellation)
            {
                case "return": target = initial; Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window,target); break;
                case "outside": target = target.WithY(timeline.Bounds.Height + 20); break;
                case "fit": timeline.Fit(); break;
                case "zoom": Avalonia.Headless.HeadlessWindowExtensions.MouseWheel(window,target,new(0,1),Avalonia.Input.RawInputModifiers.Control); break;
            }
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, target, Avalonia.Input.MouseButton.Left);
            await UntilAsync(() => !vm.IsBusy);
            Assert.Equal(revision, coordinator.Current.Revision);
        }
        finally { window.Close(); await vm.FinishAsync(); }
    }

    [Fact]
    public async Task ProjectIpc_GroupedEditorClipsMoveTogetherAndUngroupCanUndo()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "Grouped lesson");
        for (var i = 0; i < 3; i++) { await vm.StartAsync(scope.Configuration); await vm.PauseAsync(); }
        var ids = vm.Clips.Select(c => c.Id).ToArray();
        vm.SelectedClip = vm.Clips[0];
        await vm.GroupWithNextAsync();
        var group = coordinator.Current!.Clips[0].GroupId;
        Assert.NotNull(group);
        Assert.Equal(group, coordinator.Current.Clips[1].GroupId);
        Assert.Null(coordinator.Current.Clips[2].GroupId);
        await vm.MoveSelectedLaterAsync();
        Assert.Equal(new[] { ids[2], ids[0], ids[1] }, coordinator.Current.Clips.Select(c => c.Id));
        await vm.UngroupSelectedAsync();
        Assert.All(coordinator.Current.Clips, c => Assert.Null(c.GroupId));
        await vm.UndoAsync();
        Assert.Equal(group, coordinator.Current.Clips[1].GroupId);
        Assert.Equal(group, coordinator.Current.Clips[2].GroupId);
        await vm.SaveAsync();
        Assert.Equal(coordinator.Current.Revision, coordinator.SavedRevision);
        await vm.FinishAsync();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task ProjectIpc_NativeTimelineSeeksWithoutChangingSelectionAndDragCommitsOnce()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "Timeline gestures");
        for (var i = 0; i < 2; i++) { await vm.StartAsync(scope.Configuration); await vm.PauseAsync(); }
        var timeline = new ScreenRecorder.UI.Projects.Editor.ProjectTimelineControl { DataContext = vm };
        var window = new Avalonia.Controls.Window { Content = timeline };
        window.Show(); window.UpdateLayout();
        try
        {
            var width = timeline.Bounds.Width;
            var first = vm.Clips[0].Id;
            var second = vm.Clips[1].Id;
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, new(16 + (width - 32) / 4, 24), Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, new(16 + (width - 32) / 4, 24), Avalonia.Input.MouseButton.Left);
            Assert.InRange(vm.PlayheadTicks, 4_999_999, 5_000_001);
            var point = new Avalonia.Point(16 + (width - 32) * 0.75, 82);
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, point, Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, point, Avalonia.Input.MouseButton.Left);
            Assert.Equal(second, vm.SelectedClip!.Id);
            Assert.InRange(vm.PlayheadTicks, 4_999_999, 5_000_001);
            var revision = coordinator.Current!.Revision;
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, point, Avalonia.Input.MouseButton.Left);
            for (var i = 0; i < 100; i++)
                Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, new(point.X - (point.X - 20) * i / 99, 82));
            Assert.Equal(revision, coordinator.Current.Revision);
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, new(20,82), Avalonia.Input.MouseButton.Left);
            await UntilAsync(() => !vm.IsBusy && coordinator.Current.Revision > revision);
            Assert.Equal(revision + 1, coordinator.Current.Revision);
            Assert.Equal(new[] { second, first }, vm.Clips.Select(c => c.Id));
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, new(200,82), Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, new(width-20,82));
            Avalonia.Headless.HeadlessWindowExtensions.KeyPressQwerty(window, Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, new(width-20,82), Avalonia.Input.MouseButton.Left);
            Assert.Equal(revision + 1, coordinator.Current.Revision);
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, new(18,82), Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, new(18 + (width-32)/4,82));
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, new(18 + (width-32)/4,82), Avalonia.Input.MouseButton.Left);
            await UntilAsync(() => !vm.IsBusy);
            Assert.Equal(500, coordinator.Current.Clips[0].InPts);
            await vm.UndoAsync();
            Assert.Equal(0, coordinator.Current.Clips[0].InPts);
            revision = coordinator.Current.Revision;
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, new(18,82), Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, new(18 + (width-32)/4,82));
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, new(20,82));
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, new(20,82), Avalonia.Input.MouseButton.Left);
            await UntilAsync(() => !vm.IsBusy);
            Assert.Equal(revision, coordinator.Current.Revision); // Returning to origin cancels the draft.
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, new(18,82), Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, new(18 + (width-32)/4,82));
            timeline.Fit();
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, new(18 + (width-32)/4,82), Avalonia.Input.MouseButton.Left);
            await UntilAsync(() => !vm.IsBusy);
            Assert.Equal(revision, coordinator.Current.Revision); // Changing viewport invalidates captured coordinates.
        }
        finally { window.Close(); await vm.FinishAsync(); }
    }

    [Fact]
    public async Task ProjectIpc_TimelineSplitRangeDeleteAndUndoUseSavedSourceTiming()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var client = new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) }));
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "Timeline");
        for (var i = 0; i < 2; i++) { await vm.StartAsync(scope.Configuration); await vm.PauseAsync(); }
        Assert.Equal(20_000_000, vm.DurationTicks);
        Assert.Equal(new long[] { 0, 10_000_000 }, vm.TimelineClips.Select(c => c.StartTicks));
        vm.Seek(15_000_000);
        vm.SelectedClip = vm.Clips[0];
        Assert.Equal(15_000_000, vm.PlayheadTicks); // selection must not seek
        await vm.SplitAtPlayheadAsync();
        Assert.Equal(3, coordinator.Current!.Clips.Length);
        Assert.Equal(500, coordinator.Current.Clips[1].OutPts);
        Assert.Equal(500, coordinator.Current.Clips[2].InPts);
        vm.SetRangeStart(5_000_000);
        vm.SetRangeEnd(17_500_000);
        await vm.DeleteRangeAsync();
        Assert.Equal(7_500_000, vm.DurationTicks);
        Assert.Equal(500, coordinator.Current.Clips[0].OutPts);
        Assert.Equal(750, coordinator.Current.Clips[1].InPts);
        Assert.Equal(coordinator.Current.Revision, coordinator.SavedRevision);
        Assert.Equal(2, coordinator.Current.Sources.Length);
        await vm.UndoAsync();
        Assert.Equal(20_000_000, vm.DurationTicks);
        Assert.Equal(3, vm.Clips.Count);
        await vm.UndoAsync();
        Assert.Equal(2, vm.Clips.Count);
        vm.Seek(long.MaxValue);
        Assert.Equal(20_000_000, vm.PlayheadTicks);
        Assert.False(vm.CanSplit);
        await vm.FinishAsync();
    }

    [Fact]
    public async Task ProjectIpc_EditorReordersDeletesAndRestoresRealSavedClips()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var client = new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) }));
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "Edit lesson");
        for (var i = 0; i < 3; i++) { await vm.StartAsync(scope.Configuration); await vm.PauseAsync(); }
        var ids = vm.Clips.Select(c => c.Id).ToArray();
        vm.SelectedClip = vm.Clips[2];
        await vm.MoveSelectedEarlierAsync();
        Assert.Equal(new[] { ids[0], ids[2], ids[1] }, coordinator.Current!.Clips.Select(c => c.Id));
        Assert.Equal(ids[2], vm.SelectedClip!.Id);
        await vm.MoveSelectedLaterAsync();
        Assert.Equal(ids, coordinator.Current.Clips.Select(c => c.Id));
        await vm.DeleteSelectedAsync();
        Assert.Equal(new[] { ids[0], ids[1] }, coordinator.Current.Clips.Select(c => c.Id));
        Assert.Equal(coordinator.Current.Revision, coordinator.SavedRevision);
        Assert.Equal(3, coordinator.Current.Sources.Length);
        await vm.UndoAsync();
        Assert.Equal(ids, coordinator.Current.Clips.Select(c => c.Id));
        await vm.RedoAsync();
        Assert.Equal(new[] { ids[0], ids[1] }, coordinator.Current.Clips.Select(c => c.Id));
        await vm.FinishAsync();
        Assert.All(scope.Factory.Paths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task ProjectIpc_NewContentReplayAndExportStatusRemainQueryable()
    {
        await using var scope = new RecordingScope();
        var export = new ControlledContentExporter();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub(), export);
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var lost = true;
        var client = new ProjectClient(async (command, request, ct) =>
        {
            var response = await dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) });
            if (command == "StartNewRecordingContent" && lost) { lost = false; return new() { TimedOut = true }; }
            return response;
        });
        var start = new ProjectRequest { OperationId = Guid.NewGuid(), Configuration = scope.Configuration };
        var reply = await client.SendAsync("StartNewRecordingContent", start);
        Assert.True(reply.Success, reply.Error);
        Assert.True((await client.SendAsync("StartNewRecordingContent", start)).Success);
        Assert.Single(scope.Factory.Paths);
        var finish = new ProjectRequest { ProjectId = reply.State.ProjectId, OperationId = Guid.NewGuid() };
        Assert.True((await client.SendAsync("FinishRecordingContent", finish)).Success);
        await export.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var status = await client.SendAsync("GetProjectStatus", new()).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(RecordingExportState.Running, status.State.Export!.State);
        Assert.False((await client.SendAsync("CancelRecordingContentExport", finish with
            { OperationId = Guid.NewGuid(), ExportId = Guid.NewGuid() })).Success);
        export.DeferCancellation = true;
        try
        {
            Assert.True((await client.SendAsync("CancelRecordingContentExport", finish with
                { OperationId = Guid.NewGuid(), ExportId = status.State.Export.ExportId }).WaitAsync(TimeSpan.FromSeconds(2))).Success);
            var canceling = await client.SendAsync("GetProjectStatus", new()).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(RecordingExportState.Running, canceling.State.Export!.State);
        }
        finally { export.ReleaseCancellation.TrySetResult(); }
        await UntilAsync(() => coordinator.ExportStatus!.State == RecordingExportState.Canceled);
        Assert.True((await coordinator.CloseAsync()).Success);
        dispatcher = new ProjectIpcDispatcher(coordinator);
        Assert.False((await client.SendAsync("StartNewRecordingContent", start)).Success);
        Assert.Null(coordinator.Current);
    }

    [Fact]
    public async Task ProjectIpc_ClipEditLostReplyIsAppliedOnceAndRejectsChangedOrStaleRequest()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        Assert.True((await coordinator.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await coordinator.PauseAsync(Guid.NewGuid())).Success);
        var clip = coordinator.Current!.Clips.Single();
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var loseFirst = true;
        var client = new ProjectClient(async (command, request, ct) => {
            var response = await dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) });
            if (command == "ApplyProjectEdit" && loseFirst) { loseFirst = false; return new() { TimedOut = true }; }
            return response;
        });
        var revision = coordinator.Current.Revision;
        var request = new ProjectRequest { ProjectId = coordinator.Current.ProjectId, OperationId = Guid.NewGuid(),
            ExpectedRevision = revision, Edit = new ProjectClipEdit.Split(clip.Id, (clip.InPts + clip.OutPts) / 2, Guid.NewGuid()) };
        var reply = await client.SendAsync("ApplyProjectEdit", request);
        Assert.True(reply.Success, reply.Error);
        Assert.Equal(revision + 1, reply.State.SavedRevision);
        Assert.Equal(2, coordinator.Current.Clips.Length);
        Assert.True((await client.SendAsync("ApplyProjectEdit", request)).Success);
        Assert.Equal(revision + 1, coordinator.Current.Revision);
        Assert.False((await client.SendAsync("ApplyProjectEdit", request with { Edit = new ProjectClipEdit.Remove(clip.Id) })).Success);
        Assert.False((await client.SendAsync("ApplyProjectEdit", request with { OperationId = Guid.NewGuid() })).Success);
        Assert.True((await coordinator.UndoAsync(coordinator.Current.Revision)).Success);
        Assert.Single(coordinator.Current.Clips);
        Assert.Single(coordinator.Current.Sources);
    }

    [Fact]
    public async Task ProjectIpc_ClipEditSaveFailureRetainsDirtyUndoAndDoesNotReplayMutation()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        Assert.True((await coordinator.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await coordinator.PauseAsync(Guid.NewGuid())).Success);
        var backup = Path.Combine(coordinator.ProjectDirectory!, "project.opencam.bak");
        File.Move(backup, backup + ".preserved");
        Directory.CreateDirectory(backup);
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var clip = coordinator.Current!.Clips.Single();
        var request = new ProjectRequest { ProjectId = coordinator.Current.ProjectId, OperationId = Guid.NewGuid(),
            ExpectedRevision = coordinator.Current.Revision, Edit = new ProjectClipEdit.Remove(clip.Id) };
        var message = new IpcMessage { MessageType = "ApplyProjectEdit", PayloadJson = JsonSerializer.Serialize(request) };
        Assert.False((await dispatcher.DispatchAsync(message)).Success);
        Assert.True(coordinator.IsDirty);
        Assert.True(coordinator.CanUndo);
        Assert.Empty(coordinator.Current.Clips);
        var revision = coordinator.Current.Revision;
        Assert.False((await dispatcher.DispatchAsync(message)).Success);
        Assert.Equal(revision, coordinator.Current.Revision);
        Directory.Delete(backup);
        Assert.True((await coordinator.SaveAsync(revision)).Success);
        Assert.False(coordinator.IsDirty);
        Assert.True((await coordinator.UndoAsync(revision)).Success);
        Assert.Single(coordinator.Current.Clips);
    }

    [Fact]
    public async Task ProjectIpc_UnknownEditDiscriminatorIsRejectedWithoutMutation()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "Lesson")).Success);
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var reply = await dispatcher.DispatchAsync(new() { MessageType = "ApplyProjectEdit",
            PayloadJson = "{\"Edit\":{\"kind\":\"System.IO.File\"}}" });
        Assert.False(reply.Success);
        Assert.Equal(0, coordinator.Current!.Revision);
    }

    [Fact]
    public async Task ProjectIpc_RestartedRecorder_DoesNotReplayPendingCommand()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var client = new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) }));
        var request = new ProjectRequest { OperationId = Guid.NewGuid(), Path = scope.Configuration.OutputDirectory, Name = "Lesson" };
        Assert.True((await client.SendAsync("CreateProject", request)).Success);
        Assert.True((await coordinator.CloseAsync()).Success);
        dispatcher = new ProjectIpcDispatcher(coordinator); // a new lifetime has no operation cache
        var reply = await client.SendAsync("CreateProject", request);
        Assert.False(reply.Success);
        Assert.Null(coordinator.Current);
        Assert.Single(Directory.GetDirectories(scope.Configuration.OutputDirectory));
    }

    [Fact]
    public async Task ProjectIpc_RequestLostBeforeDispatch_RefreshRetriesOriginalOperationAndUnlocks()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var dropped = false;
        var saves = new List<Guid>();
        var client = new ProjectClient(async (command, request, ct) => {
            if (command == "SaveProject")
            {
                saves.Add(request.OperationId);
                if (!dropped) { dropped = true; return new() { TimedOut = true }; }
            }
            return await dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) });
        });
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "Lesson");
        await vm.SaveAsync();
        await vm.RefreshAsync();
        Assert.False(vm.StatusUnconfirmed);
        Assert.True(vm.CanEdit);
        Assert.Equal(2, saves.Count);
        Assert.Equal(saves[0], saves[1]);
        Assert.True(await vm.CloseAsync());
    }

    [Fact]
    public async Task ProjectIpc_OversizedPayloadRejectedWithoutMutation()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        Assert.False((await dispatcher.DispatchAsync(new() { MessageType = "CreateProject", PayloadJson = new string(' ', 65537) })).Success);
        Assert.Null(coordinator.Current);
        Assert.Empty(Directory.GetDirectories(scope.Configuration.OutputDirectory));
    }

    [Fact]
    public async Task ProjectIpc_AuthenticatedLifecycle_RejectsForeignProject_AndDeduplicatesRequests()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var pipe = "ocp-" + Guid.NewGuid().ToString("N")[..16];
        var key = AuthenticatedIpc.CreateKey();
        await using var server = new NamedPipeIpcServer(pipe, key, dispatcher.DispatchAsync);
        server.Start();
        await using var ipc = new NamedPipeIpcClient(pipe, key);
        var client = new ProjectClient(async (command, request, ct) => {
            var response = await ipc.SendCommandAsync(command, request, 5000, ct);
            Assert.True(response.DataJson is not null, response.ErrorMessage);
            return response;
        });
        var create = new ProjectRequest { OperationId = Guid.NewGuid(), Path = scope.Configuration.OutputDirectory, Name = "Lesson" };
        var first = await client.SendAsync("CreateProject", create);
        Assert.True(first.Success, first.Error);
        var duplicate = await client.SendAsync("CreateProject", create);
        Assert.Equal(first.State.ProjectId, duplicate.State.ProjectId);
        var wrong = await client.SendAsync("StartProjectRecording", new ProjectRequest {
            ProjectId = Guid.NewGuid(), OperationId = Guid.NewGuid(), Configuration = scope.Configuration });
        Assert.False(wrong.Success);
        Assert.Empty(scope.Factory.Paths);
        var start = new ProjectRequest { ProjectId = first.State.ProjectId, OperationId = Guid.NewGuid(), Configuration = scope.Configuration };
        Assert.True((await client.SendAsync("StartProjectRecording", start)).Success);
        Assert.True((await client.SendAsync("StartProjectRecording", start)).Success);
        Assert.Single(scope.Factory.Paths);
        var queried = await client.SendAsync("GetProjectStatus", start);
        Assert.True(queried.OperationKnown);
        Assert.Equal(ProjectMode.Recording, queried.State.Mode);
        var page = await client.SendAsync("GetProjectClips", new ProjectRequest { ProjectId = first.State.ProjectId, Limit = 101 });
        Assert.False(page.Success);
        var unknown = await dispatcher.DispatchAsync(new IpcMessage { MessageType = "DeleteProject", PayloadJson = "{}" });
        Assert.False(unknown.Success);
    }

    [Fact]
    public async Task ProjectIpc_WrongAuthenticationCannotCreateProject()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var pipe = "ocp-" + Guid.NewGuid().ToString("N")[..16];
        await using var server = new NamedPipeIpcServer(pipe, AuthenticatedIpc.CreateKey(), dispatcher.DispatchAsync);
        server.Start();
        await using var client = new NamedPipeIpcClient(pipe, AuthenticatedIpc.CreateKey());
        Assert.False((await client.SendCommandAsync("CreateProject", new ProjectRequest {
            OperationId = Guid.NewGuid(), Path = scope.Configuration.OutputDirectory, Name = "Forbidden" })).Success);
        Assert.Null(coordinator.Current);
        Assert.Empty(Directory.GetDirectories(scope.Configuration.OutputDirectory));
    }

    [Fact]
    public async Task ProjectIpc_LostResponseQueriesSameOperation_DoesNotRestartRecording()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var loseResponse = false;
        var starts = 0;
        var client = new ProjectClient(async (command, request, ct) => {
            if (command == "StartProjectRecording") starts++;
            var response = await dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) });
            return command == "StartProjectRecording" && loseResponse ? new() { TimedOut = true } : response;
        });
        var create = await client.SendAsync("CreateProject", new() { OperationId = Guid.NewGuid(), Path = scope.Configuration.OutputDirectory, Name = "Lesson" });
        loseResponse = true;
        var reply = await client.SendAsync("StartProjectRecording", new() { ProjectId = create.State.ProjectId, OperationId = Guid.NewGuid(), Configuration = scope.Configuration });
        Assert.True(reply.Success);
        Assert.False(reply.Unconfirmed);
        Assert.Equal(1, starts);
        Assert.Single(scope.Factory.Paths);
    }
}
