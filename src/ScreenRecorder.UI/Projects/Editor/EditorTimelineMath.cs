// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using ScreenRecorder.Core.Projects;
namespace ScreenRecorder.UI.Projects.Editor;
public static class EditorTimelineMath
{
    public static long StepFrame(long ticks, int direction, long duration, ProjectRational fps)
    {
        if (fps.Numerator <= 0 || fps.Denominator <= 0) throw new ArgumentOutOfRangeException(nameof(fps));
        if (duration <= 0) return 0;
        ticks = Math.Clamp(ticks, 0, duration);
        var scale = (BigInteger)TimeSpan.TicksPerSecond * fps.Denominator;
        var last = (BigInteger)(duration - 1) * fps.Numerator / scale;
        var index = direction > 0 ? (BigInteger)ticks * fps.Numerator / scale + 1 :
            ticks == 0 ? BigInteger.Zero : (BigInteger)(ticks - 1) * fps.Numerator / scale;
        index = BigInteger.Min(last, BigInteger.Max(0, index));
        return (long)((index * scale + fps.Numerator - 1) / fps.Numerator);
    }
    public static long Snap(long ticks, IEnumerable<long> targets, long tolerance, long duration)
    {
        ticks = Math.Clamp(ticks, 0, Math.Max(0, duration));
        return targets.Where(t => t >= 0 && t <= duration && Math.Abs(t - ticks) <= tolerance)
            .OrderBy(t => Math.Abs(t - ticks)).ThenBy(t => t).Cast<long?>().FirstOrDefault() ?? ticks;
    }
}
