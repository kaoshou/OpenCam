// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.Views;

internal sealed record CaptureDisplayDescriptor(
    int Index,
    CaptureRegion Bounds,
    double Scaling,
    bool IsPrimary,
    string? Identity = null);

internal sealed record UiScreenDescriptor(
    CaptureRegion Bounds,
    double Scaling,
    bool IsPrimary,
    string? Identity = null,
    bool UsesLogicalBounds = false);

internal sealed record DisplayMatchResult(
    IReadOnlyList<(int CaptureIndex, int UiScreenIndex)> Matches,
    IReadOnlyList<int> UnresolvedCaptureIndices);

internal static class DisplayScreenMatcher
{
    private const double PixelTolerance = 4;
    private const double LogicalTolerance = 2;

    public static DisplayMatchResult Match(
        IReadOnlyList<CaptureDisplayDescriptor> captureDisplays,
        IReadOnlyList<UiScreenDescriptor> uiScreens)
    {
        var candidates = captureDisplays.Select(capture =>
            Enumerable.Range(0, uiScreens.Count)
                .Where(index => IsCandidate(capture, uiScreens[index]))
                .ToArray()).ToArray();

        var matches = new List<(int CaptureIndex, int UiScreenIndex)>();
        var unresolved = new List<int>();
        for (var capturePosition = 0; capturePosition < captureDisplays.Count; capturePosition++)
        {
            var possibleScreens = candidates[capturePosition];
            if (possibleScreens.Length == 1 &&
                candidates.Count(other => other.Contains(possibleScreens[0])) == 1)
            {
                matches.Add((captureDisplays[capturePosition].Index, possibleScreens[0]));
            }
            else
            {
                unresolved.Add(captureDisplays[capturePosition].Index);
            }
        }

        return new DisplayMatchResult(matches, unresolved);
    }

    private static bool IsCandidate(CaptureDisplayDescriptor capture, UiScreenDescriptor screen)
    {
        if (capture.IsPrimary != screen.IsPrimary)
        {
            return false;
        }

        var hasCaptureIdentity = !string.IsNullOrWhiteSpace(capture.Identity);
        var hasScreenIdentity = !string.IsNullOrWhiteSpace(screen.Identity);
        if (hasCaptureIdentity && hasScreenIdentity)
        {
            return string.Equals(capture.Identity, screen.Identity, StringComparison.OrdinalIgnoreCase);
        }

        return screen.UsesLogicalBounds
            ? SameLogicalBounds(capture, screen)
            : SameBounds(capture.Bounds, screen.Bounds, PixelTolerance);
    }

    private static bool SameLogicalBounds(CaptureDisplayDescriptor capture, UiScreenDescriptor screen)
    {
        if (!double.IsFinite(capture.Scaling) || capture.Scaling <= 0)
        {
            return false;
        }

        return Close(capture.Bounds.X / capture.Scaling, screen.Bounds.X, LogicalTolerance) &&
               Close(capture.Bounds.Y / capture.Scaling, screen.Bounds.Y, LogicalTolerance) &&
               Close(capture.Bounds.Width / capture.Scaling, screen.Bounds.Width, LogicalTolerance) &&
               Close(capture.Bounds.Height / capture.Scaling, screen.Bounds.Height, LogicalTolerance);
    }

    private static bool SameBounds(CaptureRegion left, CaptureRegion right, double tolerance) =>
        Close(left.X, right.X, tolerance) &&
        Close(left.Y, right.Y, tolerance) &&
        Close(left.Width, right.Width, tolerance) &&
        Close(left.Height, right.Height, tolerance);

    private static bool Close(double left, double right, double tolerance) =>
        Math.Abs(left - right) <= tolerance;
}
