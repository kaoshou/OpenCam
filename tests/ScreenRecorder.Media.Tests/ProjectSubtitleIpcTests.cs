// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.IPC;
namespace ScreenRecorder.Media.Tests;

public class ProjectSubtitleIpcTests
{
    [Fact]
    public async Task SubtitlePayloadTraversesActualAuthenticatedMediaPipe()
    {
        var name = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());
        var key = AuthenticatedIpc.CreateKey();
        var bytes = new byte[200000]; Random.Shared.NextBytes(bytes);
        var id = Guid.NewGuid();
        await using var server = new NamedPipeIpcServer(name, key, _ => Task.FromResult(
            new ProjectReply(true, null, ProjectSnapshot.Closed) { SubtitleTrackId = id, SubtitleData = bytes }));
        server.Start();
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await pipe.ConnectAsync(deadline.Token);
        var nonce = await AuthenticatedIpc.ReadChallengeAsync(pipe, key, deadline.Token);
        await AuthenticatedIpc.WriteAsync(pipe, key, nonce, false, new IpcMessage { MessageType = "GetProjectFrame", PayloadJson = "{}" }, deadline.Token);
        var reply = await AuthenticatedMediaIpc.ReadAsync(pipe, key, nonce, deadline.Token);
        Assert.True(reply.Success);
        Assert.Equal(id, reply.SubtitleTrackId);
        Assert.Equal(bytes, reply.SubtitleData);
    }

    [Fact]
    public async Task LargeSubtitleDataUsesAuthenticatedBinaryPayloadNotMetadata()
    {
        var key = AuthenticatedIpc.CreateKey();
        var nonce = new byte[32]; Random.Shared.NextBytes(nonce);
        var bytes = new byte[200000]; Random.Shared.NextBytes(bytes);
        var id = Guid.NewGuid();
        using var stream = new MemoryStream();
        await AuthenticatedMediaIpc.WriteAsync(stream, key, nonce, new(true, null, ProjectSnapshot.Closed)
            { SubtitleTrackId = id, SubtitleData = bytes }, default);
        stream.Position = 0;
        var reply = await AuthenticatedMediaIpc.ReadAsync(stream, key, nonce, default);
        Assert.Equal(id, reply.SubtitleTrackId);
        Assert.Equal(bytes, reply.SubtitleData);
        Assert.Null(reply.Frame);
        var corrupt = stream.ToArray(); corrupt[^33] ^= 1;
        using var altered = new MemoryStream(corrupt);
        await Assert.ThrowsAsync<InvalidDataException>(() => AuthenticatedMediaIpc.ReadAsync(altered, key, nonce, default));
    }
}
