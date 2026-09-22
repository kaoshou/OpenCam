// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using ScreenRecorder.Infrastructure.IPC;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class IpcTests
{
    [Fact]
    public void SessionPipeName_OnUnix_FitsMacOsUnixSocketPath()
    {
        var name = SessionPipeNameFactory.Create(isWindows: false);
        var path = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + name);

        Assert.StartsWith("oc_", name);
        Assert.True(Encoding.UTF8.GetByteCount(path) <= 103, path);
    }

    [Fact]
    public void SessionPipeName_OnWindows_PreservesExistingPrefix()
    {
        var name = SessionPipeNameFactory.Create(isWindows: true);

        Assert.StartsWith(NamedPipeConstants.PipeBaseName + "_", name);
    }

    [Fact]
    public async Task UnixSafeSessionPipe_Communicates()
    {
        var name = SessionPipeNameFactory.Create(isWindows: false);
        await using var server = new NamedPipeIpcServer(
            name,
            _ => Task.FromResult(new IpcResponse { Success = true }));

        server.Start();
        await Task.Delay(100);

        await using var client = new NamedPipeIpcClient(name);
        var response = await client.SendCommandAsync("Ping", new { }, 2000);

        Assert.True(response.Success, response.ErrorMessage);
    }

    [Fact]
    public async Task NamedPipe_BackToBackConnections_DoNotBreakBetweenCommands()
    {
        var name = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());
        await using var server = new NamedPipeIpcServer(
            name,
            message => Task.FromResult(new IpcResponse
            {
                Success = true,
                ErrorMessage = message.MessageType
            }));

        server.Start();
        await Task.Delay(100);

        await using var client = new NamedPipeIpcClient(name);
        for (var index = 0; index < 500; index++)
        {
            var command = "Command" + index;
            var response = await client.SendCommandAsync(
                command,
                new { },
                timeoutMs: 2000);

            Assert.True(
                response.Success,
                $"{command} failed: {response.ErrorMessage}");
            Assert.Equal(command, response.ErrorMessage);
        }
    }

    [Fact]
    public async Task NamedPipe_ClientServerCommunication_ShouldSucceed()
    {
        var pipeName = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());

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
