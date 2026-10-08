// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.Projects;

[assembly: AvaloniaTestApplication(typeof(ScreenRecorder.Media.Tests.ProjectLayoutApp))]

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectLayoutApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<ProjectLayoutApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class ProjectWorkspaceLayoutTests
{
    [AvaloniaTheory]
    [InlineData(1440, 900, true, true)]
    [InlineData(1280, 720, true, false)]
    [InlineData(1024, 640, false, false)]
    public async Task ApprovedEditorRegionsUseClientBounds(int width, int height, bool left, bool right)
    {
        var window = new SizedProjectWorkspace { DataContext = new ProjectWorkspaceViewModel(new ClosedProjectClient()),
            Width = width, Height = height };
        try
        {
            window.Show();
            window.Width = width; window.Height = height;
            window.ResizeClient(new Size(width, height));
            window.UpdateLayout();
            Assert.Equal(width, window.ClientSize.Width);
            Assert.Equal(height, window.ClientSize.Height);
            var preview = window.FindControl<Border>("PreviewPane");
            var timeline = window.FindControl<Border>("TimelinePane");
            Assert.NotNull(preview); Assert.NotNull(timeline);
            Assert.True(preview.Bounds.Width >= 500);
            Assert.True(timeline.Bounds.Height >= 190);
            Assert.Equal(left, window.FindControl<Border>("ClipPane")!.IsVisible);
            Assert.Equal(right, window.FindControl<Border>("InspectorPane")!.IsVisible);
            Assert.True(preview.TranslatePoint(default, window)!.Value.Y < timeline.TranslatePoint(default, window)!.Value.Y);
        }
        finally { await window.RequestCloseAsync(); }
    }

    // Catches inspector content escaping its card/footer at supported window sizes.
    [AvaloniaTheory]
    [InlineData(1024, 640, AppLanguage.ZhTw)]
    [InlineData(1120, 740, AppLanguage.ZhTw)]
    [InlineData(1024, 640, AppLanguage.EnUs)]
    [InlineData(1120, 740, AppLanguage.EnUs)]
    public async Task InspectorActionsRemainInsideCardAndReachable(int width, int height, AppLanguage language)
    {
        var previous = LanguageManager.Instance.CurrentLanguage;
        LanguageManager.Instance.CurrentLanguage = language;
        var vm = new ProjectWorkspaceViewModel(new ClosedProjectClient());
        var window = new SizedProjectWorkspace { DataContext = vm, Width = width, Height = height };
        try
        {
            window.Show();
            window.Width = width;
            window.Height = height;
            window.ResizeClient(new Size(width, height));
            if (!window.FindControl<Border>("InspectorPane")!.IsVisible)
                window.FindControl<Button>("PropertiesToggle")!.RaiseEvent(
                    new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            window.UpdateLayout();
            var recent = window.FindControl<ComboBox>("RecentProjects")!;
            var card = recent.GetVisualAncestors().OfType<Border>().First();
            var action = card.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, vm.Strings["ProjectOpen"]));
            var scroll = recent.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll is not null)
            {
                scroll.ScrollToEnd();
                window.UpdateLayout();
            }
            var position = action.TranslatePoint(default, card)!.Value;
            Assert.True(position.Y >= 0 && position.Y + action.Bounds.Height <= card.Bounds.Height,
                $"Recent-open action at {position.Y}..{position.Y + action.Bounds.Height} escapes card height {card.Bounds.Height}.");
            Assert.True(action.Bounds.Width > 0 && action.Bounds.Height >= 36);
            if (scroll is not null && scroll.Extent.Height > scroll.Viewport.Height)
            {
                var bar = scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
                    .Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical &&
                        b.GetVisualAncestors().OfType<ScrollViewer>().First() == scroll);
                var barLeft = bar.TranslatePoint(default, scroll)!.Value.X;
                var safeText = card.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => t.Text == vm.Strings["ProjectSourceSafe"]);
                var textRight = safeText.TranslatePoint(default, scroll)!.Value.X + safeText.Bounds.Width;
                Assert.True(textRight <= barLeft, $"Inspector text ends at {textRight}, underneath scrollbar starting at {barLeft}.");
            }
        }
        finally
        {
            await window.RequestCloseAsync();
            LanguageManager.Instance.CurrentLanguage = previous;
        }
    }

    private sealed class SizedProjectWorkspace : ProjectWorkspaceView
    {
        // Avalonia 11.2.5 makes its native resize callback private-protected.
        // Test-only reflection drives the same callback as the platform; production has no test hook.
        public void ResizeClient(Size size) => typeof(Window).GetMethod("HandleResized",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(this, [size, WindowResizeReason.User]);
    }

    private sealed class ClosedProjectClient : IProjectClient
    {
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProjectReply(true, null, ProjectSnapshot.Closed));
    }
}
