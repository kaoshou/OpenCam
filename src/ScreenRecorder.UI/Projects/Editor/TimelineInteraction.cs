// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects.Editor;

public enum TimelineHit { LeftTrim, Move, RightTrim }
public sealed class TimelineInteraction
{
    private ProjectClipEdit? _proposal;
    public void Begin(ProjectClipEdit proposal) => _proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
    public void Update(ProjectClipEdit proposal)
    {
        if (_proposal is null) throw new InvalidOperationException("No active drag.");
        _proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
    }
    public ProjectClipEdit? Commit()
    {
        var result = _proposal;
        _proposal = null;
        return result;
    }
    public void Cancel() => _proposal = null;
    public static TimelineHit HitTest(double x, double width)
    {
        if (!double.IsFinite(x) || !double.IsFinite(width) || width <= 0 || x < 0 || x > width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (x <= Math.Min(10, width / 2)) return TimelineHit.LeftTrim;
        return x >= Math.Max(width - 10, width / 2) ? TimelineHit.RightTrim : TimelineHit.Move;
    }
}
