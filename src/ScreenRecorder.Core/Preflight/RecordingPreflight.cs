// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Preflight;

public enum PreflightPhase { Preparing, Checking, TrialRecording, Reviewing, Stopping, Closed, Failed }
public enum PreflightSourceState { Disabled, Waiting, Live, Silent, Unavailable }
public sealed record PreflightSourceResult(PreflightSourceState State, double Rms = 0, double Peak = 0);

public static class PreflightReadiness
{
    public static PreflightSourceResult Resolve(bool enabled, AudioLevelSample? sample, DateTimeOffset now)
    {
        if (!enabled) return new(PreflightSourceState.Disabled);
        if (sample is null) return new(PreflightSourceState.Waiting);
        var value = sample.Value;
        if (value.CapturedAt > now || now - value.CapturedAt > TimeSpan.FromSeconds(1) ||
            !double.IsFinite(value.Rms) || !double.IsFinite(value.Peak))
            return new(PreflightSourceState.Unavailable);
        return new(value.Rms < .04 ? PreflightSourceState.Silent : PreflightSourceState.Live,
            Math.Clamp(value.Rms, 0, 1), Math.Clamp(value.Peak, 0, 1));
    }
}
