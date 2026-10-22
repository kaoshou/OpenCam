// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
namespace ScreenRecorder.Media.Capture;

public interface INativeVideoCapture : IAsyncDisposable
{
    Stream Output { get; }
    CaptureSelection Selection { get; }
    event EventHandler<string>? CaptureFailed;
    Task BeginAsync(CancellationToken token);
    Task StopAsync();
}

public interface INativeVideoPlatformProvider
{
    Task<INativeVideoCapture> PrepareNativeVideoAsync(RecordingConfiguration config,
        CaptureRegion bounds, CaptureSelection? pinned, CancellationToken token);
    string BuildNativeInput(RecordingConfiguration config, string videoInput, bool directMic,
        string? systemAudio, string? microphoneAudio);
    string BuildNativeOutput(RecordingConfiguration config, string outputPath);
}
