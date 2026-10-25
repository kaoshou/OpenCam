// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Media.Capture;

/// <summary>Diagnose a missing OS prerequisite before creating recording output.</summary>
public static class WindowsRecordingPrerequisites
{
    public static void Validate(Func<string, bool> exists)
    {
        var missing = new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll" }
            .Where(name => !exists(name)).ToArray();
        if (missing.Length != 0)
            throw new IOException("Microsoft Visual C++ v14 Redistributable (x64) is required for recording. " +
                "Install or repair it from https://aka.ms/vc14/vc_redist.x64.exe and restart OpenCam. Missing: " +
                string.Join(", ", missing));
    }
}
