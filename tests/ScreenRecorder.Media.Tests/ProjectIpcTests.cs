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
