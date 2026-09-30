// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Recorder.Services;

public interface IRecordingEngineFactory
{
    IScreenRecorderEngine Create(HardwareEncoderType? pinnedEncoder, CaptureSelection? pinnedCapture = null);
}

public sealed class RecordingEngineFactory(
    IFFmpegPlatformProvider provider, IEncoderSelectionService encoderSelectionService,
    ISystemAudioLoopbackCapture? systemAudioLoopbackCapture = null,
    IMicrophoneCapture? microphoneCapture = null,
    IMicrophoneLevelObserver? microphoneLevelObserver = null,
    ICapturePlanProvider? capturePlanProvider = null) : IRecordingEngineFactory
{
    public IScreenRecorderEngine Create(HardwareEncoderType? pinnedEncoder, CaptureSelection? pinnedCapture = null) => new FFmpegScreenRecorderEngine(
        provider, systemAudioLoopbackCapture, microphoneCapture: microphoneCapture,
        microphoneLevelObserver: microphoneLevelObserver,
        encoderSelectionService: encoderSelectionService, pinnedEncoder: pinnedEncoder,
        capturePlanProvider: capturePlanProvider, pinnedCapture: pinnedCapture);
}
