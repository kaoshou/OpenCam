// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Media.Capture;

public sealed record CaptureLaunchPlan(CaptureSelection Selection, string VideoInputArguments);

public interface ICapturePlanProvider
{
    Task<CaptureLaunchPlan> PrepareAsync(RecordingConfiguration config, CaptureRegion bounds,
        CaptureSelection? pinned, CancellationToken token);
    string BuildInputArguments(CaptureLaunchPlan plan, RecordingConfiguration config, bool hasDirectShowMic,
        string? systemAudioPipeArg, string? microphoneAudioPipeArg);
}
