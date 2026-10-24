// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Models;

/// <summary>Native producer clock anchor; never derived from progress delivery time.
/// Both values use microseconds. A new segment requires a new anchor.</summary>
public readonly record struct CaptureClockAnchor(long SourcePtsMicroseconds, long HostMicroseconds)
{
    public long ToHostMicroseconds(long sourcePtsMicroseconds) =>
        checked(HostMicroseconds + checked(sourcePtsMicroseconds - SourcePtsMicroseconds));
}

/// <summary>Source-relative physical pixel coordinates. Invisible samples carry no coordinates/buttons.</summary>
public readonly record struct PointerSample(long HostMicroseconds, double X, double Y, bool Visible, int Buttons)
{
    public static PointerSample FromDesktop(long time, double x, double y, CaptureRegion bounds, bool visible, int buttons)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (!bounds.IsValid) throw new ArgumentException("Invalid pointer capture bounds.", nameof(bounds));
        var inside = visible && double.IsFinite(x) && double.IsFinite(y) &&
            x >= bounds.X && y >= bounds.Y && x < (long)bounds.X + bounds.Width && y < (long)bounds.Y + bounds.Height;
        return inside ? new(time, x - bounds.X, y - bounds.Y, true, buttons) : new(time, 0, 0, false, 0);
    }
}

public static class PointerTimeline
{
    /// <summary>Validate each immutable bounded block before Resolve; never concatenate a whole recording in RAM.</summary>
    public static void Validate(IReadOnlyList<PointerSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        long previous = -1;
        foreach (var sample in samples)
        {
            if (sample.HostMicroseconds < 0 || sample.HostMicroseconds <= previous ||
                !double.IsFinite(sample.X) || !double.IsFinite(sample.Y) || sample.X < 0 || sample.Y < 0 ||
                sample.Buttons is < 0 or > 7 || (!sample.Visible && (sample.X != 0 || sample.Y != 0 || sample.Buttons != 0)))
                throw new ArgumentException("Invalid or unordered pointer samples.", nameof(samples));
            previous = sample.HostMicroseconds;
        }
    }

    /// <summary>Binary lookup in a validated block; never bridge pause/gap/visibility boundaries or extrapolate.</summary>
    public static PointerSample? Resolve(IReadOnlyList<PointerSample> samples, long hostMicroseconds)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var low = 0; var high = samples.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var sample = samples[middle];
            if (sample.HostMicroseconds == hostMicroseconds) return sample.Visible ? sample : null;
            if (sample.HostMicroseconds < hostMicroseconds) low = middle + 1; else high = middle - 1;
        }
        if (high < 0 || low >= samples.Count) return null;
        var before = samples[high]; var after = samples[low];
        var gap = after.HostMicroseconds - before.HostMicroseconds;
        if (!before.Visible || !after.Visible || gap <= 0 || gap > 100_000) return null;
        var fraction = (double)(hostMicroseconds - before.HostMicroseconds) / gap;
        return new(hostMicroseconds, before.X + (after.X - before.X) * fraction,
            before.Y + (after.Y - before.Y) * fraction, true, before.Buttons);
    }
}
