// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Diagnostics;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Services;

namespace ScreenRecorder.Media.Tests;

public class RecordingStartCoordinatorTests
{
    [Fact]
    public async Task TimedOutStart_ConfirmsRecordingBeforeReportingSuccess()
    {
        var pipeName = "OcSt" + Guid.NewGuid().ToString("N")[..10];
        var state = RecordingState.Idle;
        await using var server = new NamedPipeIpcServer(pipeName, async message =>
        {
            if (message.MessageType == "StartRecording")
            {
                await Task.Delay(800);
                state = RecordingState.Recording;
                return new IpcResponse { Success = true, SessionId = "session-1" };
            }

            return TelemetryResponse(state);
        });
        server.Start();
        await using var client = new NamedPipeIpcClient(pipeName);

        var result = await RecordingStartCoordinator.StartAsync(
            client, new RecordingConfiguration(), 250, () => false);

        Assert.True(result.Success);
        Assert.Equal("session-1", result.SessionId);
    }

    [Fact]
    public async Task TimedOutStart_ConfirmsFailureInsteadOfGuessing()
    {
        var pipeName = "OcSt" + Guid.NewGuid().ToString("N")[..10];
        var state = RecordingState.Idle;
        await using var server = new NamedPipeIpcServer(pipeName, async message =>
        {
            if (message.MessageType == "StartRecording")
            {
                await Task.Delay(800);
                state = RecordingState.Failed;
                return new IpcResponse { Success = false, ErrorMessage = "encoder failed" };
            }

            return TelemetryResponse(state, "encoder failed");
        });
        server.Start();
        await using var client = new NamedPipeIpcClient(pipeName);

        var result = await RecordingStartCoordinator.StartAsync(
            client, new RecordingConfiguration(), 250, () => false);

        Assert.False(result.Success);
        Assert.Contains("encoder failed", result.ErrorMessage);
    }

    [Fact]
    public async Task UnresponsiveRecorder_ReturnsUnconfirmedWithoutDeclaringFailure()
    {
        var pipeName = "OcSt" + Guid.NewGuid().ToString("N")[..10];
        await using var server = new NamedPipeIpcServer(pipeName, async message =>
        {
            if (message.MessageType == "StartRecording")
            {
                await Task.Delay(1200);
                return new IpcResponse { Success = true, SessionId = "session-1" };
            }

            return TelemetryResponse(RecordingState.Recording);
        });
        server.Start();
        await using var client = new NamedPipeIpcClient(pipeName);
        var stopwatch = Stopwatch.StartNew();

        var result = await RecordingStartCoordinator.StartAsync(
            client, new RecordingConfiguration(), 150, () => false,
            confirmationTimeout: TimeSpan.FromMilliseconds(150));

        Assert.False(result.Success);
        Assert.True(result.StatusUnconfirmed);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    private static IpcResponse TelemetryResponse(RecordingState state, string? error = null) =>
        new()
        {
            Success = true,
            ErrorMessage = JsonSerializer.Serialize(new RecorderTelemetry
            {
                State = state,
                SessionId = "session-1",
                LastError = error
            })
        };
}
