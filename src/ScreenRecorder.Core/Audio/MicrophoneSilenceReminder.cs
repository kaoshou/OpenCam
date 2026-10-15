// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Audio;

/// <summary>Advisory only; never controls recording or retains audio.</summary>
public sealed class MicrophoneSilenceReminder
{
    private TimeSpan? _silentSince;
    private TimeSpan? _lastObservation;

    public bool Update(bool enabledAndRecordingMicrophone, AudioMeterState state, TimeSpan elapsed)
    {
        if (!enabledAndRecordingMicrophone || state != AudioMeterState.Silent)
        {
            Reset();
            return false;
        }

        // A stalled UI/IPC connection is not evidence of continuous silence.
        if (_lastObservation is not { } previous || elapsed < previous ||
            elapsed - previous > TimeSpan.FromSeconds(1))
            _silentSince = elapsed;
        _lastObservation = elapsed;
        return elapsed - _silentSince!.Value >= TimeSpan.FromSeconds(30);
    }

    public void Reset()
    {
        _silentSince = null;
        _lastObservation = null;
    }
}
