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
    [AvaloniaFact]
    public void MainWindowWiresAndDetachesSharedWebQualityFlow()
    {
        var main = new MainViewModel(forScreenshot: true);
        var window = new MainWindow { DataContext = main };
        window.Show();
        try
        {
            Assert.NotNull(main.RequestWebPlayerExport);
            window.DataContext = null;
            Assert.Null(main.RequestWebPlayerExport);
        }
        finally { window.Close(); main.Cleanup(); }
    }

    [AvaloniaFact]
    public async Task OriginalVideoWebCommandUsesSharedQualityFlowBeforeDestination()
    {
        var client = new ProjectGifExportDialogTests.ExportClient();
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(client, () => true, () => Task.CompletedTask);
        try
        {
            await main.OpenRecordingProjectAsync("/test/project.opencam");
            var property = typeof(MainViewModel).GetProperty("RequestWebPlayerExport");
            Assert.NotNull(property);
            var ordered = new List<string>();
            var profile = new ScreenRecorder.Core.Projects.Mp4ExportOptions(
                ScreenRecorder.Core.Projects.Mp4ExportQuality.Compact);
            Func<ProjectWorkspaceViewModel, Task> export = model => model.RequestWebExportAsync(
                _ => { ordered.Add("quality"); return Task.FromResult<ScreenRecorder.Core.Projects.Mp4ExportOptions?>(profile); },
                () => { ordered.Add("folder"); return Task.FromResult<string?>("/test"); });
            property.SetValue(main, export);
            main.RequestWebPlayerDestination = () => throw new Exception("Legacy picker must not bypass quality settings");
            await main.ExportWebPlayerCommand.ExecuteAsync(null);
            Assert.Equal(new[] { "quality", "folder" }, ordered);
            var request = Assert.Single(client.Exports);
            Assert.True(request.ExportWebPlayer);
            Assert.Equal(profile, request.ExportOptions!.Mp4);
        }
        finally { main.Cleanup(); }
    }

    [AvaloniaFact]
    public async Task OriginalVideoGifCommandExportsWholeProjectThroughSharedFlow()
    {
        var client = new ProjectGifExportDialogTests.ExportClient();
        var main = new MainViewModel(forScreenshot:true);
        main.ConfigureRecordingContent(client,()=>true,()=>Task.CompletedTask);
        try
        {
            await main.OpenRecordingProjectAsync("/test/project.opencam");
            main.RequestMediaExport = (model,format)=>model.RequestExportAsync(format,null,
                _=>Task.FromResult<ScreenRecorder.Core.Projects.GifExportOptions?>(new()),
                (_,_)=>Task.FromResult<string?>("/test/whole.gif"));
            await main.ExportGifCommand.ExecuteAsync(null);
            var request=Assert.Single(client.Exports);
            Assert.Equal(ScreenRecorder.Core.Projects.ProjectExportFormat.Gif,request.ExportOptions!.Format);
            Assert.Equal(ScreenRecorder.Core.Projects.ProjectExportScope.WholeProject,request.ExportOptions.Scope);
            Assert.Null(request.ExportOptions.ClipId);
        }
        finally { main.Cleanup(); }
    }

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
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(),b => b.Flyout is MenuFlyout);
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
