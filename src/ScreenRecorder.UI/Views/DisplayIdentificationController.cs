// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.UI.Views;

internal sealed record DisplayIdentificationBadge(
    int Number,
    bool IsSelected,
    UiScreenDescriptor Screen);

internal interface IDisplayBadgePresenter
{
    IDisposable Show(DisplayIdentificationBadge badge);
}

internal interface IDisplayIdentificationTimer
{
    IDisposable Schedule(TimeSpan delay, Action callback);
}

internal sealed class DisplayIdentificationController(
    IDisplayBadgePresenter presenter,
    IDisplayIdentificationTimer timer) : IDisposable
{
    private readonly List<IDisposable> _badges = new();
    private IDisposable? _expiry;
    private long _generation;

    public int Show(
        IReadOnlyList<CaptureDisplayDescriptor> captureDisplays,
        IReadOnlyList<UiScreenDescriptor> uiScreens,
        int? selectedIndex)
    {
        Close();
        var generation = _generation;
        var match = DisplayScreenMatcher.Match(captureDisplays, uiScreens);
        try
        {
            foreach (var (captureIndex, uiScreenIndex) in match.Matches)
            {
                _badges.Add(presenter.Show(new DisplayIdentificationBadge(
                    captureIndex + 1,
                    captureIndex == selectedIndex,
                    uiScreens[uiScreenIndex])));
            }

            if (_badges.Count > 0)
            {
                _expiry = timer.Schedule(TimeSpan.FromSeconds(4), () =>
                {
                    if (_generation == generation)
                    {
                        Close();
                    }
                });
            }
        }
        catch
        {
            Close();
            throw;
        }

        return match.UnresolvedCaptureIndices.Count;
    }

    public void Close()
    {
        _generation++;
        _expiry?.Dispose();
        _expiry = null;
        foreach (var badge in _badges)
        {
            badge.Dispose();
        }
        _badges.Clear();
    }

    public void Dispose() => Close();
}
