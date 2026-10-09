// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task ProjectFrame_SeparateChannelAndResumeCancelsPendingDecode()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var decoder = new HeldPcmProcess();
        await using var frames = new ProjectFrameService(coordinator, decoder);
        var dispatcher = new ProjectIpcDispatcher(coordinator, frames: frames);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "Frame preview");
        await vm.StartAsync(scope.Configuration);
        await vm.PauseAsync();
        var request = new ProjectRequest { ProjectId = vm.State.ProjectId, ExpectedRevision = vm.State.Revision,
            ServerInstanceId = vm.State.ServerInstanceId, TimelineTicks = 0 };
        var message = new IpcMessage { MessageType = "GetProjectFrame", PayloadJson = JsonSerializer.Serialize(request) };
        Assert.False((await dispatcher.DispatchAsync(message)).Success); // Never pixels on control IPC.
        Assert.True((await dispatcher.DispatchMediaAsync(message)).Success);
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await vm.StartAsync(scope.Configuration).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(decoder.Canceled.Task.IsCompleted);
        Assert.False((await dispatcher.DispatchMediaAsync(message)).Success);
        await vm.FinishAsync();
    }

    [Fact]
    public async Task ProjectFrame_SnapshotAndBoundsAreCheckedBeforeDecoding()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var decoder = new HeldPcmProcess();
        await using var frames = new ProjectFrameService(coordinator, decoder);
        var dispatcher = new ProjectIpcDispatcher(coordinator, frames: frames);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "Frame bounds");
        await vm.StartAsync(scope.Configuration);
        await vm.PauseAsync();
        var valid = new ProjectRequest { ProjectId = vm.State.ProjectId, ExpectedRevision = vm.State.Revision,
            ServerInstanceId = vm.State.ServerInstanceId };
        foreach (var request in new[] { valid with { ExpectedRevision = -1 }, valid with { TimelineTicks = -1 },
                     valid with { ServerInstanceId = Guid.NewGuid() }, valid with { TimelineTicks = long.MaxValue } })
            Assert.False((await dispatcher.DispatchMediaAsync(new() { MessageType = "GetProjectFrame",
                PayloadJson = JsonSerializer.Serialize(request) })).Success);
        Assert.False(decoder.Started.Task.IsCompleted);
        await vm.FinishAsync();
    }
}
