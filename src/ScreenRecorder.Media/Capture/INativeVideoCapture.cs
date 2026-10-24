// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Enums;
namespace ScreenRecorder.Media.Capture;

public interface INativeVideoCapture : IAsyncDisposable
{
    Stream Output { get; }
    CaptureSelection Selection { get; }
    event EventHandler<string>? CaptureFailed;
    Task BeginAsync(CancellationToken token);
    Task StopAsync();
}

/// <summary>Private, source-relative metadata; subscribers must not send it to application logs.</summary>
public interface INativePointerCapture
{
    event EventHandler<NativePointerEvent>? PointerMetadata;
    event EventHandler<string>? PointerIncomplete;
}

public interface INativeVideoPlatformProvider
{
    bool RequiresEditablePointer => false;
    HardwareEncoderType? RequiredNativeEncoder => HardwareEncoderType.AppleVideoToolbox;
    bool UsesNamedPipeVideo => false;
    Task<INativeVideoCapture> PrepareNativeVideoAsync(RecordingConfiguration config,
        CaptureRegion bounds, CaptureSelection? pinned, CancellationToken token);
    string BuildNativeInput(RecordingConfiguration config, string videoInput, bool directMic,
        string? systemAudio, string? microphoneAudio);
    string BuildNativeOutput(RecordingConfiguration config, string outputPath);
    string BuildNativeOutput(RecordingConfiguration config, HardwareEncoderType selectedEncoder, string outputPath) =>
        BuildNativeOutput(config, outputPath);
}
