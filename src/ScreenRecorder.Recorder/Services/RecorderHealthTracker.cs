// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Recorder.Services;

public readonly record struct RecorderProgressObservation(
    bool EngineRunning,
    long Frames,
    TimeSpan RecordedTime,
    long FileSizeBytes);

public readonly record struct RecorderTelemetryHealth(
    long? DroppedFrames,
    bool? VideoHealthy,
    bool? SystemAudioHealthy,
    bool? MicrophoneHealthy,
    bool? EncoderHealthy,
    string? Warning);

/// <summary>
/// Converts observable recorder progress into conservative health state.
/// Unknown data remains null instead of being reported as healthy.
/// </summary>
public sealed class RecorderHealthTracker
{
    private AudioSourceType _audioSource;
    private bool _hasProgress;
    private long _lastFrames;
    private TimeSpan _lastRecordedTime;
    private long _lastPositiveFileSize;
    private int _unchangedFileSizeObservations;
    private bool? _videoHealthy;
    private bool? _encoderHealthy;
    private bool _audioDeviceLost;
    private string? _fatalWarning;
    private string? _progressWarning;

    public void Reset(RecordingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _audioSource = configuration.AudioSource;
        _audioDeviceLost = false;
        _fatalWarning = null;
        BeginSegment();
    }

    public void BeginSegment()
    {
        _hasProgress = false;
        _lastFrames = 0;
        _lastRecordedTime = TimeSpan.Zero;
        _lastPositiveFileSize = 0;
        _unchangedFileSizeObservations = 0;
        _videoHealthy = null;
        _encoderHealthy = _fatalWarning is null ? null : false;
        _progressWarning = null;
    }

    public void ObserveProgress(RecorderProgressObservation observation)
    {
        if (!observation.EngineRunning)
        {
            _encoderHealthy = false;
            _videoHealthy = _hasProgress ? false : null;
            _progressWarning ??= "FFmpeg process is not running.";
            return;
        }

        var hasValidProgress = observation.Frames > 0 ||
                               observation.RecordedTime > TimeSpan.Zero;

        if (hasValidProgress)
        {
            _hasProgress = true;
            if (_fatalWarning is null)
            {
                _videoHealthy = true;
                _encoderHealthy = true;
            }
        }

        if (observation.FileSizeBytes > 0 &&
            _lastPositiveFileSize > 0 &&
            observation.FileSizeBytes == _lastPositiveFileSize)
        {
            _unchangedFileSizeObservations++;
        }
        else
        {
            _unchangedFileSizeObservations = 0;
            if (_fatalWarning is null)
            {
                _progressWarning = null;
            }
        }

        if (_unchangedFileSizeObservations >= 3)
        {
            _videoHealthy = false;
            _encoderHealthy = false;
            _progressWarning = "Recording output has stopped growing.";
        }

        _lastFrames = Math.Max(_lastFrames, observation.Frames);
        _lastRecordedTime = observation.RecordedTime > _lastRecordedTime
            ? observation.RecordedTime
            : _lastRecordedTime;
        if (observation.FileSizeBytes > 0)
        {
            _lastPositiveFileSize = observation.FileSizeBytes;
        }
    }

    public void RecordEngineError(string message)
    {
        _fatalWarning = string.IsNullOrWhiteSpace(message)
            ? "The recording engine reported an error."
            : message.Trim();
        _encoderHealthy = false;
    }

    public void RecordAudioDeviceLost() => _audioDeviceLost = true;

    public RecorderTelemetryHealth CreateTelemetryHealth(AudioLevelsSnapshot audioLevels)
    {
        var systemSelected = _audioSource is
            AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone;
        var microphoneSelected = _audioSource is
            AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone;

        var systemHealthy = ResolveAudioHealth(systemSelected, audioLevels.SystemAudio.State);
        var microphoneHealthy = ResolveAudioHealth(
            microphoneSelected,
            audioLevels.Microphone.State);
        var warning = _fatalWarning ?? _progressWarning;
        if (warning is null && _audioDeviceLost)
        {
            warning = "A selected audio device is no longer available.";
        }
        else if (warning is null && (systemHealthy == false || microphoneHealthy == false))
        {
            warning = "A selected audio source is not providing fresh samples.";
        }

        return new RecorderTelemetryHealth(
            DroppedFrames: null,
            VideoHealthy: _videoHealthy,
            SystemAudioHealthy: systemHealthy,
            MicrophoneHealthy: microphoneHealthy,
            EncoderHealthy: _encoderHealthy,
            Warning: warning);
    }

    private bool? ResolveAudioHealth(bool selected, AudioMeterState state)
    {
        if (!selected)
        {
            return null;
        }

        if (_audioDeviceLost)
        {
            return false;
        }

        return state switch
        {
            AudioMeterState.Live or AudioMeterState.Silent => true,
            AudioMeterState.Paused => null,
            _ => false
        };
    }
}
