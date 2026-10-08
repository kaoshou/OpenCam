// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.UI.Projects.Editor;

public sealed record EditorLayout(double LeftWidth, double RightWidth, double TimelineHeight,
    bool LeftVisible, bool RightVisible)
{
    public static EditorLayout ForSize(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return new(220, 260, 240, width >= 1200, width > 1280 && height > 720);
    }
}
