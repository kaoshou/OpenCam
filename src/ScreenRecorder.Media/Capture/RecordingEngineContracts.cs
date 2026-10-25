// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Media.Capture;

public interface IScreenRecorderEngine : IAsyncDisposable
{
    bool IsRunning { get; }
    bool UseSyntheticCaptureSource { get; set; }
    TimeSpan CurrentRecordedTime { get; }
    long CurrentFramesRecorded { get; }
    HardwareEncoderType ActiveEncoder { get; }
    CaptureSelection? ActiveCapture { get; }
    Task StartRecordingAsync(string workingFilePath, RecordingConfiguration config, CaptureRegion actualBounds, CancellationToken cancellationToken = default);
    Task StopRecordingAsync(CancellationToken cancellationToken = default);
    event EventHandler<string>? EngineErrorOccurred;
    event EventHandler<string>? EngineWarningOccurred;
    event EventHandler? AudioDeviceLost;
}

public interface IRecordingAudioLevelSource
{
    (AudioLevelSample? SystemAudio, AudioLevelSample? Microphone) ReadInputLevels(DateTimeOffset now);
}

public interface IBoundRecordingOutput
{
    // Caller owns the exclusively-created destination and keeps it open until Stop/Dispose.
    Stream? OutputStream { get; set; }
    bool UsesDirectPreview => false;
}
