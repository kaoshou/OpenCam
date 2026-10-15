// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.UI.Recording;

namespace ScreenRecorder.Media.Tests;

public class MicrophoneWarningPresenterTests
{
    [Fact]
    public void OnlyRevealsAfterExclusionAndClosesOnClearWithoutRecreatingEveryPoll()
    {
        var window = new Warning(true);
        using var presenter = new MicrophoneWarningPresenter(() => window);
        Assert.True(presenter.Update(true));
        Assert.Equal(new[] { "invisible", "exclude", "reveal" }, window.Calls);
        Assert.True(presenter.Update(true));
        Assert.Equal(3, window.Calls.Count);
        Assert.False(presenter.Update(false));
        Assert.Equal("dispose", window.Calls[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExclusionFailureOrExceptionNeverRevealsAndDisposesTheInvisibleWindow(bool throws)
    {
        var window = new Warning(false, throws);
        using var presenter = new MicrophoneWarningPresenter(() => window);
        Assert.False(presenter.Update(true));
        Assert.Equal(new[] { "invisible", "exclude", "dispose" }, window.Calls);
        Assert.False(presenter.Update(true));
        Assert.Equal(3, window.Calls.Count);
    }

    private sealed class Warning(bool excluded, bool throws = false) : ICaptureExcludedWarning
    {
        public List<string> Calls { get; } = [];
        public void PrepareInvisible() => Calls.Add("invisible");
        public bool TryExcludeFromCapture()
        {
            Calls.Add("exclude");
            if (throws) throw new InvalidOperationException("Exclusion unavailable");
            return excluded;
        }
        public void Reveal() => Calls.Add("reveal");
        public void Dispose() => Calls.Add("dispose");
    }
}
