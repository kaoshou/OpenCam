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
    public async Task SlowMediaReaderDoesNotBlockStopAndTransfersAreSerialized()
    {
        var name = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());
        var key = AuthenticatedIpc.CreateKey();
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pixels = new byte[3840 * 2160 * 4];
        await using var media = new NamedPipeIpcServer(name + "-frames", key, _ => {
            Interlocked.Increment(ref calls); started.TrySetResult();
            return Task.FromResult(new ProjectReply(true, null, ProjectSnapshot.Closed) {
                Frame = new(0, 0, Guid.NewGuid(), pixels) { PixelWidth = 3840, PixelHeight = 2160 } });
        });
        await using var control = new NamedPipeIpcServer(name, key, _ => Task.FromResult(new IpcResponse { Success = true }));
        media.Start(); control.Start();
        using var slow = new System.IO.Pipes.NamedPipeClientStream(".", name + "-frames", System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await slow.ConnectAsync(timeout.Token);
        var nonce = await AuthenticatedIpc.ReadChallengeAsync(slow, key, timeout.Token);
        await AuthenticatedIpc.WriteAsync(slow, key, nonce, false,
            new IpcMessage { MessageType = "GetProjectFrame", PayloadJson = "{}" }, timeout.Token);
        await started.Task.WaitAsync(timeout.Token);
        await using var client = new NamedPipeIpcClient(name, key);
        var next = client.SendProjectFrameAsync(new(), timeout.Token);
        var stop = await client.SendCommandAsync("StopProjectPreview", new ProjectRequest(), timeoutMs: 500);
        Assert.True(stop.Success, stop.ErrorMessage);
        Assert.Equal(1, Volatile.Read(ref calls));
        slow.Dispose(); // Abandoning the transfer releases its sole slot.
        Assert.True((await next).Success);
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task HdPreviewTraversesMediaPipeWithoutRelaxingControlPipe(int pattern)
    {
        var name = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());
        var key = AuthenticatedIpc.CreateKey();
        var width = pattern == 4 ? 3840 : pattern == 3 ? 1920 : 1280;
        var height = pattern == 4 ? 2160 : pattern == 3 ? 1080 : 720;
        var rgba = new byte[width * height * 4];
        Random.Shared.NextBytes(rgba);
        if (pattern == 1)
            for (var i = 0; i < rgba.Length; i += 4)
            { rgba[i] = rgba[i + 1] = rgba[i + 2] = 250; rgba[i + 3] = 255; }
        if (pattern == 2) // Worst-case base64: every character is '+'.
            for (var i = 0; i < rgba.Length; i += 3)
            { rgba[i] = 251; rgba[i + 1] = 239; rgba[i + 2] = 190; }
        var response = new ProjectReply(true, null, ProjectSnapshot.Closed)
            { Frame = new(0, 0, Guid.NewGuid(), rgba) { PixelWidth = width, PixelHeight = height } };
        await using var server = new NamedPipeIpcServer(name + "-frames", key, _ => Task.FromResult(response));
        server.Start();
        await using var client = new NamedPipeIpcClient(name, key);
        var result = await client.SendProjectFrameAsync(new());
        Assert.True(result.Success, result.Error);
        var decoded = result.Frame!;
        Assert.True(decoded.HasValidPixels);
        Assert.Equal(rgba, decoded.Rgba);
        Assert.False((decoded with { PixelWidth = int.MaxValue }).HasValidPixels);
        Assert.False((decoded with { Rgba = new byte[512 * 288 * 4] }).HasValidPixels);
        await using var controlClient = new NamedPipeIpcClient(name + "-frames", key);
        Assert.False((await controlClient.SendCommandAsync("GetProjectFrame", new ProjectRequest())).Success);
    }

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
