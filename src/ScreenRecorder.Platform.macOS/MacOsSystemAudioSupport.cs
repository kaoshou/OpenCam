// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Platform.macOS;

public static class MacOsSystemAudioSupport
{
    public const string HelperFileName = "OpenCam.SystemAudio";

    public static string HelperPath(string baseDirectory) =>
        Path.Combine(baseDirectory, HelperFileName);

    public static bool IsSupported(
        Version osVersion,
        string baseDirectory) =>
        osVersion.Major >= 13 &&
        File.Exists(HelperPath(baseDirectory));
}
