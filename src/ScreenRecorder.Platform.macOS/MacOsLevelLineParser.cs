// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace ScreenRecorder.Platform.macOS;

internal static class MacOsLevelLineParser
{
    public static bool TryParse(string line, out double rms, out double peak)
    {
        rms = 0;
        peak = 0;
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 3 || fields[0] != "LEVEL" ||
            !fields[1].StartsWith("rms=", StringComparison.Ordinal) ||
            !fields[2].StartsWith("peak=", StringComparison.Ordinal) ||
            !double.TryParse(fields[1].AsSpan(4), NumberStyles.Float, CultureInfo.InvariantCulture, out rms) ||
            !double.TryParse(fields[2].AsSpan(5), NumberStyles.Float, CultureInfo.InvariantCulture, out peak))
        {
            return false;
        }

        return double.IsFinite(rms) && double.IsFinite(peak) &&
            rms is >= 0 and <= 1 && peak is >= 0 and <= 1;
    }
}
