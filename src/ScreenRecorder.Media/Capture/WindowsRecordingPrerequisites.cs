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
            throw new IOException("The bundled Microsoft Visual C++ recording runtime is missing. " +
                "Re-extract or reinstall the complete OpenCam package. Do not download individual DLLs. Missing: " +
                string.Join(", ", missing));
    }
}
