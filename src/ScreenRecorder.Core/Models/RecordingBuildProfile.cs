// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Models;

/// <summary>Immutable build policy: missing deployment files cannot enable an older recording core.</summary>
public static class RecordingBuildProfile
{
    public static bool NativeOnlyTest =>
#if OPENCAM_OBS_ONLY_TEST
        true;
#else
        false;
#endif
}
