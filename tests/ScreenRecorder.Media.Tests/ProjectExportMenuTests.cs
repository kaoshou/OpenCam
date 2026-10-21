// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectExportMenuTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClipParentOnlyExpandsAndChildrenCaptureIdRejectingStaleRevision(bool stale)
    {
        var client = new ProjectGifExportDialogTests.ExportClient();
        var vm = new ProjectWorkspaceViewModel(client); await vm.RefreshAsync();
        var calls = new List<(ScreenRecorder.Core.Projects.ProjectExportFormat, Guid?)>();
        var menu = ProjectClipExportMenu.Create(vm, client.Clips[1].Id,
            (format, clip) => { calls.Add((format,clip)); return Task.CompletedTask; });
        Assert.Null(menu.Command);
        Assert.Equal(2, menu.ItemCount);
        menu.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Empty(calls);
        if (stale) vm.State = vm.State with { Revision = 8 };
        ((MenuItem)menu.Items[1]!).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        if (stale) Assert.Empty(calls);
        else Assert.Equal((ScreenRecorder.Core.Projects.ProjectExportFormat.Gif, (Guid?)client.Clips[1].Id), Assert.Single(calls));
    }

    // Both entry points must expose GIF, not only the editor's export menu.
    [AvaloniaFact]
    public async Task OriginalAndEditorExportMenusOfferMp4ThenGifThenWeb()
    {
        var main = new MainWindow { DataContext = new MainViewModel(forScreenshot: true) };
        var editor = new ProjectWorkspaceView { DataContext = new ProjectWorkspaceViewModel(new ClosedClient()) };
        try
        {
            main.Show(); editor.Show(); main.UpdateLayout(); editor.UpdateLayout();
            foreach (var window in new Window[] { main, editor })
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>().Where(b => b.Flyout is MenuFlyout));
                var menu = (MenuFlyout)button.Flyout!;
                menu.ShowAt(button);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var items = menu.Items.Cast<MenuItem>().ToArray();
                Assert.Equal(3, items.Length);
                Assert.Contains("MP4", items[0].Header!.ToString());
                Assert.Contains("GIF", items[1].Header!.ToString());
                Assert.DoesNotContain("GIF", items[2].Header!.ToString());
                menu.Hide();
            }
        }
        finally { main.Close(); await editor.RequestCloseAsync(); }
    }

    private sealed class ClosedClient : IProjectClient
    {
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProjectReply(true, null, ProjectSnapshot.Closed));
    }
}
