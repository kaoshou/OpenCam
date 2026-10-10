// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.UI.Views;

public static class HomeWindowSizing
{
    public static double InitialHeight(double workAreaHeightPixels, double scaling, double decorationHeight)
    {
        if (!double.IsFinite(scaling) || scaling <= 0) throw new ArgumentOutOfRangeException(nameof(scaling));
        if (!double.IsFinite(workAreaHeightPixels) || workAreaHeightPixels <= 0)
            throw new ArgumentOutOfRangeException(nameof(workAreaHeightPixels));
        if (!double.IsFinite(decorationHeight) || decorationHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(decorationHeight));
        return Math.Clamp(workAreaHeightPixels / scaling - decorationHeight - 24, 1, 820);
    }
}
