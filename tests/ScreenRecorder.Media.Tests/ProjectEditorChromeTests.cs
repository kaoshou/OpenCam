// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.Projects.Editor;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectEditorChromeTests
{
    [AvaloniaFact]
    public async Task DensityLabelUpdatesWhenLanguageChangesWhileOpen()
    {
        var previous = LanguageManager.Instance.CurrentLanguage;
        LanguageManager.Instance.CurrentLanguage = AppLanguage.ZhTw;
        var vm = new ProjectWorkspaceViewModel(new TimedClipClient());
        var window = new ProjectWorkspaceView { DataContext = vm };
        try
        {
            window.Show(); window.UpdateLayout();
            var toggle = window.FindControl<Button>("ClipDensityToggle")!;
            LanguageManager.Instance.CurrentLanguage = AppLanguage.EnUs;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("Compact", toggle.GetVisualDescendants().OfType<EditorToolContent>().Single().Text);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            LanguageManager.Instance.CurrentLanguage = AppLanguage.ZhTw;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("縮圖", toggle.GetVisualDescendants().OfType<EditorToolContent>().Single().Text);
        }
        finally { await window.RequestCloseAsync(); LanguageManager.Instance.CurrentLanguage = previous; }
    }

    [AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw)]
    [InlineData(AppLanguage.EnUs)]
    public async Task CompactListHidesThumbnailsAndShrinksRowsWithoutChangingEditState(AppLanguage language)
    {
        var previous = LanguageManager.Instance.CurrentLanguage;
        LanguageManager.Instance.CurrentLanguage = language;
        var client = new TimedClipClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        var window = new ProjectWorkspaceView { DataContext = vm, Width = 1440, Height = 900 };
        try
        {
            window.Show(); window.UpdateLayout();
            var list = window.FindControl<ListBox>("ClipList")!;
            vm.SelectedClip = vm.Clips[2];
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var selectedRow = list.GetVisualDescendants().OfType<ListBoxItem>().Single(row => row.IsSelected);
            var presenter = selectedRow.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First();
            Assert.True(presenter.Background is null || presenter.Background is Avalonia.Media.ISolidColorBrush brush && brush.Color.A == 0,
                "Fluent selection overlay must not cover the card's pale-blue background.");
            vm.Seek(100);
            Assert.Equal(100, vm.PlayheadTicks); // The fixture must include timing, not only clip names.
            var state = vm.State;
            var card = list.GetVisualDescendants().OfType<ProjectClipCard>().First();
            var normalHeight = card.Bounds.Height;
            Assert.True(card.GetVisualDescendants().OfType<Image>().Single().IsVisible);
            var toggle = window.FindControl<Button>("ClipDensityToggle");
            Assert.NotNull(toggle);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            card = list.GetVisualDescendants().OfType<ProjectClipCard>().First();
            Assert.False(card.GetVisualDescendants().OfType<Image>().Single().IsVisible);
            Assert.True(card.Bounds.Height <= normalHeight * .6, $"Compact {card.Bounds.Height}, normal {normalHeight}, requested {card.Height}, desired {card.DesiredSize}, measure={card.IsMeasureValid}, arrange={card.IsArrangeValid}, parent={card.Parent?.GetType().Name}");
            Assert.Same(vm.Clips[2], list.SelectedItem);
            Assert.Equal(100, vm.PlayheadTicks);
            Assert.Equal(state, vm.State);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(card.GetVisualDescendants().OfType<Image>().Single().IsVisible);
            Assert.Same(vm.Clips[2], vm.SelectedClip);
        }
        finally { await window.RequestCloseAsync(); LanguageManager.Instance.CurrentLanguage = previous; }
    }

    [AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw, 1024)]
    [InlineData(AppLanguage.EnUs, 1024)]
    [InlineData(AppLanguage.ZhTw, 1440)]
    [InlineData(AppLanguage.EnUs, 1440)]
    public async Task ToolbarActionsHaveIconsAndRemainReachable(AppLanguage language, int width)
    {
        var previous = LanguageManager.Instance.CurrentLanguage;
        LanguageManager.Instance.CurrentLanguage = language;
        var vm = new ProjectWorkspaceViewModel(new ProjectWorkspaceClipLoadingTests.ClipClient());
        await vm.RefreshAsync();
        var window = new ProjectWorkspaceView { DataContext = vm, Width = width, Height = 900 };
        try
        {
            window.Show(); window.UpdateLayout();
            var pane = window.FindControl<Border>("TimelinePane")!;
            var buttons = pane.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.True(buttons.Length >= 9);
            foreach (var button in buttons)
            {
                Assert.Contains(button.GetVisualDescendants().OfType<Path>(), p => p.Data is not null);
                var origin = button.TranslatePoint(default, pane)!.Value;
                Assert.True(origin.X >= 0 && origin.X + button.Bounds.Width <= pane.Bounds.Width + 1);
                Assert.True(button.Bounds.Height >= 32);
            }
            var clipPane = window.FindControl<Border>("ClipPane")!;
            var wasVisible = clipPane.IsVisible;
            window.FindControl<Button>("ClipsToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotEqual(wasVisible, clipPane.IsVisible);
        }
        finally { await window.RequestCloseAsync(); LanguageManager.Instance.CurrentLanguage = previous; }
    }

    private sealed class TimedClipClient : IProjectClient
    {
        private readonly ProjectWorkspaceClipLoadingTests.ClipClient _inner = new();
        public async Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            var reply = await _inner.SendAsync(command, request, ct);
            return reply.Clips is null ? reply : reply with { TimelineClips = reply.Clips.Select((clip, i) =>
                new ProjectTimelineClip(clip.Id, (request.Offset + i) * TimeSpan.TicksPerSecond,
                    (request.Offset + i + 1) * TimeSpan.TicksPerSecond)).ToArray() };
        }
    }
}
