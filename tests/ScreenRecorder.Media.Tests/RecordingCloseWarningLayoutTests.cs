// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public sealed class RecordingCloseWarningLayoutTests
{
    [AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw, false)]
    [InlineData(AppLanguage.EnUs, false)]
    [InlineData(AppLanguage.JaJp, false)]
    [InlineData(AppLanguage.ZhTw, true)]
    [InlineData(AppLanguage.EnUs, true)]
    [InlineData(AppLanguage.JaJp, true)]
    public void WarningActionsCenterTheirLabels(AppLanguage language, bool unconfirmed)
    {
        var strings = LanguageManager.Instance;
        var previous = strings.CurrentLanguage;
        strings.CurrentLanguage = language;
        var window = new RecordingCloseWarningWindow(unconfirmed);
        try
        {
            window.Show(); window.UpdateLayout();
            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            foreach (var button in buttons)
            {
                Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
                Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
                if (!button.IsVisible) continue;
                var label = button.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => t.Text == button.Content?.ToString());
                var point = label.TranslatePoint(default, button)!.Value;
                Assert.InRange(Math.Abs(point.X + label.Bounds.Width / 2 - button.Bounds.Width / 2), 0, 1);
                Assert.InRange(Math.Abs(point.Y + label.Bounds.Height / 2 - button.Bounds.Height / 2), 0, 1);
            }
        }
        finally { window.Close(); strings.CurrentLanguage = previous; }
    }
}
