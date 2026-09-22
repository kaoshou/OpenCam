// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Interfaces;

public record MicrophoneCaptureInfo(
    string PipePath,
    int SampleRate,
    int Channels,
    string FfmpegInputArgs);

public interface IMicrophoneCapture : IAsyncDisposable
{
    bool IsSupported { get; }
    bool IsCapturing { get; }

    Task<MicrophoneCaptureInfo?> StartCaptureAsync(
        string? deviceId,
        CancellationToken cancellationToken = default);

    Task StopCaptureAsync(CancellationToken cancellationToken = default);

    event EventHandler<string>? AudioErrorOccurred;
}
