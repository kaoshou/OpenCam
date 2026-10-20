using System.Text.Json;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public class PreflightIpcTests
{
    [Fact]
    public async Task ResumeConfirmationRequiresUnchangedSettingsAndResolvedDevices()
    {
        var live = new Live(); live.Ready.SetResult();
        await using var coordinator = new PreflightCoordinator(new(), () => live);
        var endpoint = "original";
        await using var dispatcher = new PreflightIpcDispatcher(coordinator, config => endpoint + ":" + config.Fps);
        var config = new RecordingConfiguration { Fps = 30 };
        var id = Guid.NewGuid();
        var begun = JsonSerializer.Deserialize<PreflightReply>((await dispatcher.DispatchAsync(Message("BeginPreflight", new(id, Configuration: config)))).DataJson!)!;
        var request = new PreflightRequest(id, begun.Status.ConfigurationRevision, begun.ServerInstanceId);
        using (var ready = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            while (System.Text.Json.JsonSerializer.Deserialize<PreflightReply>(
                (await dispatcher.DispatchAsync(Message("GetPreflightStatus", request))).DataJson!)!.Status.Phase != PreflightPhase.Checking)
                await Task.Delay(10, ready.Token);
        Assert.True((await dispatcher.DispatchAsync(Message("EndPreflight", request with { Confirm = true }))).Success);
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            while (coordinator.CurrentOperation is not null) await Task.Delay(10, deadline.Token);
        async Task<bool> Confirm(RecordingConfiguration configuration) => JsonSerializer.Deserialize<PreflightReply>(
            (await dispatcher.DispatchAsync(Message("CheckPreflightConfirmation", request with { Configuration = configuration }))).DataJson!)!.ConfigurationConfirmed;
        Assert.True(await Confirm(config));
        Assert.False(await Confirm(new() { Fps = 60 }));
        endpoint = "replacement";
        Assert.False(await Confirm(config));
    }
    [Fact]
    public async Task AuthenticatedControlAndMediaPipesPreservePreflightFrameEnvelope()
    {
        var live = new Live(); live.Ready.SetResult();
        await using var coordinator = new PreflightCoordinator(new(), () => live);
        await using var dispatcher = new PreflightIpcDispatcher(coordinator);
        var name = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());
        var key = AuthenticatedIpc.CreateKey();
        await using var control = new NamedPipeIpcServer(name, key, dispatcher.DispatchAsync);
        await using var media = new NamedPipeIpcServer(name + "-frames", key, dispatcher.DispatchMediaAsync);
        control.Start(); media.Start();
        await using var client = new NamedPipeIpcClient(name, key);
        var id = Guid.NewGuid();
        var response = await client.SendCommandAsync("BeginPreflight", new PreflightRequest(id, Configuration: new()));
        Assert.True(response.Success, response.ErrorMessage);
        var begun = JsonSerializer.Deserialize<PreflightReply>(response.DataJson!)!;
        var request = new PreflightRequest(id, begun.Status.ConfigurationRevision, begun.ServerInstanceId);
        var frame = await client.SendPreflightFrameAsync(request);
        Assert.True(frame.Success, frame.Error);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, frame.Frame!.Rgba);
        await using var unauthenticated = new NamedPipeIpcClient(name, AuthenticatedIpc.CreateKey());
        Assert.False((await unauthenticated.SendCommandAsync("EndPreflight", request)).Success);
        Assert.Equal(id, coordinator.CurrentOperation);
        Assert.True((await client.SendCommandAsync("EndPreflight", request)).Success);
    }
    private static IpcMessage Message(string command, PreflightRequest request) => new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) };
    [Fact]
    public async Task PreparingReturnsPromptlyAndFrameIdentityIsScopedToServerAndOperation()
    {
        var live = new Live();
        var capture = new RecordingCaptureLease();
        await using var coordinator = new PreflightCoordinator(capture, () => live);
        await using var dispatcher = new PreflightIpcDispatcher(coordinator);
        var id = Guid.NewGuid();
        var begin = await dispatcher.DispatchAsync(Message("BeginPreflight", new(id, Configuration: new()))).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(begin.Success, begin.ErrorMessage);
        var reply = JsonSerializer.Deserialize<PreflightReply>(begin.DataJson!)!;
        Assert.Equal(PreflightPhase.Preparing, reply.Status.Phase);
        live.Ready.TrySetResult();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (coordinator.ReadCurrentStatus().Phase != PreflightPhase.Checking) await Task.Delay(10, deadline.Token);
        var request = new PreflightRequest(id, reply.Status.ConfigurationRevision, reply.ServerInstanceId);
        var frame = await dispatcher.DispatchMediaAsync(Message("GetPreflightFrame", request));
        Assert.True(frame.Success, frame.Error);
        Assert.Equal(id, frame.Frame!.ClipId);
        Assert.Equal(reply.ServerInstanceId, frame.State.ServerInstanceId);
        Assert.False((await dispatcher.DispatchMediaAsync(Message("GetPreflightFrame", request with { OperationId = Guid.NewGuid() }))).Success);
        Assert.False((await dispatcher.DispatchAsync(Message("EndPreflight", request with { ServerInstanceId = Guid.NewGuid() }))).Success);
        Assert.True(capture.IsOwned);
        Assert.True((await dispatcher.DispatchAsync(Message("EndPreflight", request))).Success);
        while (capture.IsOwned) await Task.Delay(10, deadline.Token);
        Assert.False(capture.IsOwned);
        Assert.False((await dispatcher.DispatchMediaAsync(Message("GetPreflightFrame", request))).Success);
    }
    [Fact]
    public async Task RejectsUnknownPropertiesAndOversizedRequestsBeforeCapture()
    {
        var capture = new RecordingCaptureLease();
        await using var coordinator = new PreflightCoordinator(capture, () => new Live());
        await using var dispatcher = new PreflightIpcDispatcher(coordinator);
        foreach (var json in new[] { "{\"OperationId\":\"" + Guid.NewGuid() + "\",\"OutputPath\":\"/tmp/foreign\"}", new string('x', 65537) })
            Assert.False((await dispatcher.DispatchAsync(new() { MessageType = "BeginPreflight", PayloadJson = json })).Success);
        Assert.False(capture.IsOwned);
    }
    private sealed class Live : IPreflightLiveSession
    {
        public TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartAsync(RecordingConfiguration config, CancellationToken ct) => Ready.Task.WaitAsync(ct);
        public PreflightStatus ReadStatus(DateTimeOffset now) => PreflightStatus.Empty with { HasFrame = true, AudioReady = true, Phase = PreflightPhase.Checking };
        public ProjectFrameReply? ReadFrame() => new(0, 0, Guid.Empty, [255, 0, 0, 255]) { PixelWidth = 1, PixelHeight = 1 };
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
