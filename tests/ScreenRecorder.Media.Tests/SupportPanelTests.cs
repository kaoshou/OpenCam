// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public sealed class SupportPanelTests
{
    [AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw)] [InlineData(AppLanguage.EnUs)] [InlineData(AppLanguage.JaJp)]
    public void SettingsAndAboutExposeLocalizedSupportWithoutStartingExternalActions(AppLanguage language)
    {
        var old = LanguageManager.Instance.CurrentLanguage; LanguageManager.Instance.CurrentLanguage = language;
        var vm = new SettingsViewModel(new MockSettingsService()); vm.ShowAboutTab();
        using var about = new WindowScope(new AboutWindow());
        using var settings = new WindowScope(new SettingsWindow(vm));
        try
        {
            foreach (var window in new Window[] { about.Window, settings.Window })
            {
                var width = window.Width; var height = window.Height;
                window.Show(); window.Width = width; window.Height = height;
                // Drive the native resize callback, matching the existing editor layout tests.
                typeof(Window).GetMethod("HandleResized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, new object[] { new Size(width, height), WindowResizeReason.User });
                window.UpdateLayout();
                Assert.Equal(width, window.ClientSize.Width);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<SupportPanel>());
                foreach (var key in new[] { "SupportCheckUpdates", "SupportOpenLogs", "SupportCopyEnvironment", "SupportExport" })
                {
                    var text = LanguageManager.Instance[key]; Assert.NotEqual(key, text);
                    var button = panel.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == text);
                    Assert.True(button.IsEffectivelyEnabled); Assert.NotNull(ToolTip.GetTip(button));
                    var origin = button.TranslatePoint(default, panel)!.Value;
                    Assert.InRange(origin.X + button.Bounds.Width, 0, panel.Bounds.Width + 1);
                }
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == LanguageManager.Instance["SupportPrivacy"]);
                if (Environment.GetEnvironmentVariable("OPENCAM_SUPPORT_SCREENSHOT_DIR") is { } directory)
                {
                    Directory.CreateDirectory(directory);
                    using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
                    image.Render(window);
                    image.Save(Path.Combine(directory, $"{window.GetType().Name}-{language}.png"));
                }
            }
        }
        finally { LanguageManager.Instance.CurrentLanguage = old; }
    }
    private sealed class WindowScope(Window window) : IDisposable
    {
        public Window Window => window;
        public void Dispose() => window.Close();
    }
}
