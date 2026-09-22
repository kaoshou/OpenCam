// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public sealed class DisplayIdentificationControllerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Show_LabelsEveryUniquelyMatchedScreen(int count)
    {
        var presenter = new FakePresenter();
        var timer = new FakeTimer();
        var controller = new DisplayIdentificationController(presenter, timer);
        var capture = Enumerable.Range(0, count)
            .Select(index => Capture(index, index * 1920, index == 3 ? -100 : 0))
            .ToArray();
        var ui = capture.Reverse()
            .Select(item => new UiScreenDescriptor(item.Bounds, 1, item.IsPrimary, item.Identity))
            .ToArray();

        var unresolved = controller.Show(capture, ui, selectedIndex: count - 1);

        Assert.Equal(0, unresolved);
        Assert.Equal(Enumerable.Range(1, count), presenter.Active.Select(item => item.Badge.Number).Order());
        Assert.Single(presenter.Active.Where(item => item.Badge.IsSelected));
        Assert.Equal(count, presenter.Active.Single(item => item.Badge.IsSelected).Badge.Number);
        Assert.Equal(1, timer.ActiveCount);
    }

    [Fact]
    public void RepeatShow_ReplacesOldBadgesAndStaleExpiryCannotCloseNewBadges()
    {
        var presenter = new FakePresenter();
        var timer = new FakeTimer();
        var controller = new DisplayIdentificationController(presenter, timer);
        var capture = new[] { Capture(0, 0, 0) };
        var ui = new[] { new UiScreenDescriptor(capture[0].Bounds, 1, true) };

        controller.Show(capture, ui, 0);
        var oldBadge = Assert.Single(presenter.Active);
        var oldTimer = timer.Latest!;
        controller.Show(capture, ui, 0);

        Assert.True(oldBadge.IsClosed);
        Assert.True(oldTimer.IsClosed);
        Assert.Single(presenter.Active);
        oldTimer.FireEvenIfClosed();
        Assert.Single(presenter.Active);
        timer.Latest!.FireEvenIfClosed();
        Assert.Empty(presenter.Active);
    }

    [Fact]
    public void Close_OnDisplayChangeOrRecordingPreparation_RemovesEveryBadge()
    {
        var presenter = new FakePresenter();
        var timer = new FakeTimer();
        var controller = new DisplayIdentificationController(presenter, timer);
        var capture = new[] { Capture(0, 0, 0), Capture(1, 1920, 0) };
        var ui = capture.Select(item => new UiScreenDescriptor(item.Bounds, 1, item.IsPrimary)).ToArray();

        controller.Show(capture, ui, 0);
        controller.Close();

        Assert.Empty(presenter.Active);
        Assert.Equal(0, timer.ActiveCount);
    }

    [Fact]
    public void AmbiguousOrDisconnectedScreen_IsNotLabeled()
    {
        var presenter = new FakePresenter();
        var controller = new DisplayIdentificationController(presenter, new FakeTimer());
        var capture = new[]
        {
            new CaptureDisplayDescriptor(0, new CaptureRegion(0, 0, 1920, 1080), 1, false),
            new CaptureDisplayDescriptor(1, new CaptureRegion(0, 0, 1920, 1080), 1, false)
        };
        var ui = new[] { new UiScreenDescriptor(capture[0].Bounds, 1, false) };

        var unresolved = controller.Show(capture, ui, 0);

        Assert.Equal(2, unresolved);
        Assert.Empty(presenter.Active);
    }

    private static CaptureDisplayDescriptor Capture(int index, int x, int y) =>
        new(index, new CaptureRegion(x, y, 1920, 1080), 1, index == 0);

    private sealed class FakePresenter : IDisplayBadgePresenter
    {
        public List<FakeHandle> Handles { get; } = new();
        public IReadOnlyList<FakeHandle> Active => Handles.Where(item => !item.IsClosed).ToArray();

        public IDisposable Show(DisplayIdentificationBadge badge)
        {
            var handle = new FakeHandle(badge);
            Handles.Add(handle);
            return handle;
        }
    }

    private sealed class FakeHandle(DisplayIdentificationBadge badge) : IDisposable
    {
        public DisplayIdentificationBadge Badge { get; } = badge;
        public bool IsClosed { get; private set; }
        public void Dispose() => IsClosed = true;
    }

    private sealed class FakeTimer : IDisplayIdentificationTimer
    {
        public FakeTimerHandle? Latest { get; private set; }
        public int ActiveCount => Latest is { IsClosed: false } ? 1 : 0;
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            Assert.Equal(TimeSpan.FromSeconds(4), delay);
            Latest = new FakeTimerHandle(callback);
            return Latest;
        }
    }

    private sealed class FakeTimerHandle(Action callback) : IDisposable
    {
        public bool IsClosed { get; private set; }
        public void Dispose() => IsClosed = true;
        public void FireEvenIfClosed() => callback();
    }
}
