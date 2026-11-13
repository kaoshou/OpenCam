// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Headless.XUnit;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public sealed class MainViewModelLifetimeTests
{
    [AvaloniaFact]
    public void CleanedUpModelStopsReactingToGlobalLanguageWhileActiveModelStillUpdates()
    {
        var strings = LanguageManager.Instance;
        var previous = strings.CurrentLanguage;
        strings.CurrentLanguage = AppLanguage.ZhTw;
        var closed = new MainViewModel(forScreenshot: true);
        var active = new MainViewModel(forScreenshot: true);
        var closedChanges = 0;
        var activeChanges = 0;
        closed.PropertyChanged += (_, _) => closedChanges++;
        active.PropertyChanged += (_, _) => activeChanges++;
        try
        {
            closed.Cleanup();
            closed.Cleanup(); // Cleanup is safe on repeated close paths.
            closedChanges = 0;
            strings.CurrentLanguage = AppLanguage.EnUs;
            Assert.True(activeChanges > 0);
            Assert.Equal(0, closedChanges);
        }
        finally
        {
            active.Cleanup();
            closed.Cleanup();
            strings.CurrentLanguage = previous;
        }
    }
}
