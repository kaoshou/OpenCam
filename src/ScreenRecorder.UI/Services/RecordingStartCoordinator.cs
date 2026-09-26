// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.UI.Services;

/// <summary>
/// A timed-out IPC reply does not cancel recorder startup. Keep settings locked
/// until the recorder reports a definitive state, so recording cannot continue
/// after the UI has incorrectly declared startup failed.
/// </summary>
internal static class RecordingStartCoordinator
{
    public static async Task<IpcResponse> StartAsync(
        NamedPipeIpcClient client,
        RecordingConfiguration configuration,
        int startTimeoutMs,
        Func<bool> recorderExited,
        TimeSpan? confirmationTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var response = await SendWithHardTimeoutAsync(
            client,
            "StartRecording",
            configuration,
            startTimeoutMs,
            cancellationToken);
        if (!response.TimedOut)
        {
            return response;
        }

        // The server handles lifecycle commands serially. A telemetry reply
        // therefore arrives only after the timed-out StartRecording has ended.
        var confirmationLimit = confirmationTimeout ?? TimeSpan.FromSeconds(30);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (recorderExited())
            {
                return new IpcResponse
                {
                    Success = false,
                    ErrorMessage = "Recorder process exited while confirming recording startup."
                };
            }

            var remaining = confirmationLimit - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return new IpcResponse { StatusUnconfirmed = true };
            }

            var telemetryTimeoutMs = (int)Math.Clamp(
                remaining.TotalMilliseconds,
                1,
                1500);
            var telemetryResponse = await SendWithHardTimeoutAsync(
                client,
                "GetTelemetry",
                new { },
                telemetryTimeoutMs,
                cancellationToken);
            if (telemetryResponse.Success &&
                !string.IsNullOrWhiteSpace(telemetryResponse.ErrorMessage))
            {
                RecorderTelemetry? telemetry = null;
                try
                {
                    telemetry = JsonSerializer.Deserialize<RecorderTelemetry>(
                        telemetryResponse.ErrorMessage);
                }
                catch (JsonException)
                {
                    // A malformed response is not proof that recording failed.
                }

                if (telemetry is not null)
                {
                    if (telemetry.State is RecordingState.Recording or RecordingState.Paused)
                    {
                        return new IpcResponse
                        {
                            Success = true,
                            SessionId = telemetry.SessionId
                        };
                    }

                    if (telemetry.State is RecordingState.Failed or RecordingState.Idle or
                        RecordingState.Completed or RecordingState.Interrupted or RecordingState.Recoverable)
                    {
                        return new IpcResponse
                        {
                            Success = false,
                            ErrorMessage = telemetry.LastError ?? "Recorder did not start recording."
                        };
                    }
                }
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private static async Task<IpcResponse> SendWithHardTimeoutAsync(
        NamedPipeIpcClient client,
        string messageType,
        object payload,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var boundedTimeoutMs = Math.Max(1, timeoutMs);
        try
        {
            return await client.SendCommandAsync(
                    messageType,
                    payload,
                    boundedTimeoutMs,
                    cancellationToken)
                .WaitAsync(
                    TimeSpan.FromMilliseconds(boundedTimeoutMs),
                    cancellationToken);
        }
        catch (TimeoutException)
        {
            return new IpcResponse
            {
                Success = false,
                TimedOut = true,
                ErrorMessage = $"{messageType} did not respond within {boundedTimeoutMs} ms."
            };
        }
    }
}
