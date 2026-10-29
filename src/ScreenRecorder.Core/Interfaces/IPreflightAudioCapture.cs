// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;

namespace ScreenRecorder.Core.Interfaces;

public interface IPreflightAudioCapture : IAsyncDisposable
{
    Task StartAsync(RecordingConfiguration configuration, Stream? trialOutput, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    PreflightSourceResult ReadMicrophone(DateTimeOffset now);
    PreflightSourceResult ReadSystemAudio(DateTimeOffset now);
    AudioLevelSample? ReadMix(DateTimeOffset now) => null;
    bool IsRunning { get; }
    string? ErrorCode { get; }
}
