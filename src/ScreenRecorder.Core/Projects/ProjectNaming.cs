// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;

namespace ScreenRecorder.Core.Projects;

public static class ProjectNaming
{
    // Callers prefix the result with OpenCam and append a unique ID. Display names
    // remain unchanged; this is only a portable, bounded component of a filename.
    public static string FileStem(string name)
    {
        ProjectValidation.ValidateName(name);
        var result = new StringBuilder();
        foreach (var rune in name.EnumerateRunes())
        {
            var part = Rune.IsLetterOrDigit(rune) || rune.Value is ' ' or '-' or '_'
                ? rune.ToString() : "-";
            if (part == "-" && (result.Length == 0 || result[^1] == '-')) continue;
            if (result.Length + part.Length > 40) break;
            result.Append(part);
        }
        var value = result.ToString().Trim(' ', '-', '_');
        return value.Length == 0 ? "OpenCam" : value;
    }
}
