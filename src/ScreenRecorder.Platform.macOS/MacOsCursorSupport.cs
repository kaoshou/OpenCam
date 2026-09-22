// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Platform.macOS;

public static class MacOsCursorSupport
{
    public const string HelperFileName = "OpenCam.CursorOverlay";

    public static string HelperPath(string baseDirectory) =>
        Path.Combine(baseDirectory, HelperFileName);
}
