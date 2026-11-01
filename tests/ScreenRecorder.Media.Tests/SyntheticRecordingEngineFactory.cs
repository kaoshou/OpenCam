// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.Encoders;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Testing;

// Test-only dependency for generated lavfi media. Production factories must keep
// their native runtime requirements, including on headless Linux CI workers.
internal sealed class SyntheticRecordingEngineFactory(
    IFFmpegPlatformProvider provider,
    ISystemAudioLoopbackCapture? systemAudioLoopbackCapture = null,
    IMicrophoneCapture? microphoneCapture = null,
    IMicrophoneLevelObserver? microphoneLevelObserver = null,
    IEncoderSelectionService? encoderSelectionService = null,
    ICapturePlanProvider? capturePlanProvider = null) : IRecordingEngineFactory
{
    public IScreenRecorderEngine Create(HardwareEncoderType? pinnedEncoder, CaptureSelection? pinnedCapture = null) =>
        new FFmpegScreenRecorderEngine(provider, systemAudioLoopbackCapture,
            microphoneCapture: microphoneCapture, microphoneLevelObserver: microphoneLevelObserver,
            encoderSelectionService: encoderSelectionService ?? new FFmpegEncoderDetector(provider),
            pinnedEncoder: pinnedEncoder, capturePlanProvider: capturePlanProvider,
            pinnedCapture: pinnedCapture is { RecordingCore: "ffmpeg" }
                ? pinnedCapture with { RecordingCore = null } : pinnedCapture)
        {
            UseSyntheticCaptureSource = true
        };
}
