// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.IO.Pipes;
using ScreenRecorder.Infrastructure.IPC;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class IpcSecurityTests
{
    [Fact]
    public async Task WrongKeyAndWrongPeerNeverDispatch_ValidClientStillWorks()
    {
        var name = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());
        var key = AuthenticatedIpc.CreateKey();
        var dispatched = 0;
        await using var server = new NamedPipeIpcServer(name, key, _ =>
        {
            Interlocked.Increment(ref dispatched);
            return Task.FromResult(new IpcResponse { Success = true });
        });
        server.Start();
        await using var wrong = new NamedPipeIpcClient(name, AuthenticatedIpc.CreateKey());
        Assert.False((await wrong.SendCommandAsync("StartRecording", new { })).Success);
        await using var wrongPeer = new NamedPipeIpcClient(name, key, int.MaxValue);
        Assert.False((await wrongPeer.SendCommandAsync("StartRecording", new { })).Success);
        Assert.Equal(0, dispatched);
        await using var valid = new NamedPipeIpcClient(name, key);
        Assert.True((await valid.SendCommandAsync("Ping", new { })).Success);
        Assert.Equal(1, dispatched);
    }

    [Fact]
    public async Task IdleAndOversizedPeersDoNotBlockStop()
    {
        var name = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());
        var key = AuthenticatedIpc.CreateKey();
        await using var server = new NamedPipeIpcServer(name, key,
            _ => Task.FromResult(new IpcResponse { Success = true }));
        server.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var idle = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await idle.ConnectAsync(timeout.Token);
        await AuthenticatedIpc.ReadChallengeAsync(idle, key, timeout.Token);
        using var oversized = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await oversized.ConnectAsync(timeout.Token);
        await AuthenticatedIpc.ReadChallengeAsync(oversized, key, timeout.Token);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, int.MaxValue);
        await oversized.WriteAsync(header, timeout.Token);
        await oversized.FlushAsync(timeout.Token);
        await using var client = new NamedPipeIpcClient(name, key);
        Assert.True((await client.SendCommandAsync("StopRecording", new { }, 1000)).Success);
    }

    [Fact]
    public async Task ReplayAndReflectionAndModifiedFramesFail()
    {
        var key = AuthenticatedIpc.CreateKey();
        var firstNonce = AuthenticatedIpc.CreateKey();
        using var frame = new MemoryStream();
        await AuthenticatedIpc.WriteAsync(frame, key, firstNonce, false,
            new IpcMessage { MessageType = "StartRecording" }, default);
        frame.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            AuthenticatedIpc.ReadAsync<IpcMessage>(frame, key, AuthenticatedIpc.CreateKey(), false, default));
        frame.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            AuthenticatedIpc.ReadAsync<IpcMessage>(frame, key, firstNonce, true, default));
        var bytes = frame.ToArray();
        bytes[8] ^= 1;
        using var changed = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            AuthenticatedIpc.ReadAsync<IpcMessage>(changed, key, firstNonce, false, default));
    }

    [Fact]
    public async Task MissingBootstrapAndSelfParentAreRejected()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => RecorderBootstrap.ReadAsync(["--daemon"]));
        await Assert.ThrowsAsync<InvalidDataException>(() => RecorderBootstrap.ReadAsync(
            ["--daemon", "--parent-pid", Environment.ProcessId.ToString()]));
    }
}
