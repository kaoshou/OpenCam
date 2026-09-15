using ScreenRecorder.Infrastructure.IPC;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class IpcTests
{
    [Fact]
    public async Task NamedPipe_ClientServerCommunication_ShouldSucceed()
    {
        var pipeName = "TestPipe_" + Guid.NewGuid().ToString("N");

        await using var server = new NamedPipeIpcServer(pipeName, message =>
        {
            if (message.MessageType == "Ping")
            {
                return Task.FromResult(new IpcResponse { Success = true });
            }
            if (message.MessageType == "Echo")
            {
                return Task.FromResult(new IpcResponse { Success = true, SessionId = message.PayloadJson });
            }
            return Task.FromResult(new IpcResponse { Success = false, ErrorMessage = "Unknown" });
        });

        server.Start();

        // 等待伺服器管線初始化就緒
        await Task.Delay(100);

        await using var client = new NamedPipeIpcClient(pipeName);

        // 測試 Ping
        var pingResponse = await client.SendCommandAsync("Ping", new { }, timeoutMs: 2000);
        Assert.True(pingResponse.Success);

        // 測試 Echo
        var echoResponse = await client.SendCommandAsync("Echo", "hello-world", timeoutMs: 2000);
        Assert.True(echoResponse.Success);
        Assert.Contains("hello-world", echoResponse.SessionId);
    }
}
