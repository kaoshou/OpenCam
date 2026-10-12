using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public class FocusedEditorSafetyTests
{
    [AvaloniaFact]
    public async Task HomeShowsProjectTotalWithoutWrappingAtTwentyFourHours()
    {
        var client = new Client();
        client.State = client.State with { HasUnsavedEdits = false,
            TotalRecordingTicks = new TimeSpan(39, 2, 3).Ticks };
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(client, () => true, () => Task.CompletedTask);
        try
        {
            Assert.Equal("00:00:00", main.TotalRecordingTimeText);
            await main.OpenRecordingProjectAsync("/test/project.opencam");
            Assert.Equal("39:02:03", main.TotalRecordingTimeText);
            await main.CloseRecordingProjectAsync();
            Assert.Equal("00:00:00", main.TotalRecordingTimeText);
        }
        finally { main.Cleanup(); }
    }

    [AvaloniaFact]
    public void ContentControllerCannotBypassVisibleEditorLock()
    {
        var main = new MainViewModel(forScreenshot: true);
        main.ConfigureRecordingContent(new Client(), () => true, () => Task.CompletedTask);
        try
        {
            Assert.True(main.CanStartRecording);
            main.IsProjectWorkspaceOpen = true;
            Assert.False(main.CanStartRecording);
            Assert.False(main.CanManageRecordingProject);
            Assert.False(main.CanExportRecordingContent);
            main.IsProjectWorkspaceOpen = false;
            Assert.True(main.CanStartRecording);
        }
        finally { main.Cleanup(); }
    }

    [AvaloniaFact]
    public async Task ReturnHonorsCancelAndKeepsEditorVisible()
    {
        var client = new Client();
        var vm = new ProjectWorkspaceViewModel(client);
        vm.ApplyReply(new(true, null, client.State));
        var asked = 0;
        vm.RequestUnsavedDecision = () => { asked++; return Task.FromResult(UnsavedDecision.Cancel); };
        var home = new Window();
        var editor = new ProjectWorkspaceView { DataContext = vm };
        home.Show(); editor.ShowForRecordingWindow(home);
        try
        {
            Assert.False(home.IsEnabled);
            await editor.ReturnToRecordingAsync();
            Assert.Equal(1, asked);
            Assert.True(editor.IsVisible);
        }
        finally
        {
            vm.ApplyReply(new(true, null, client.State with { HasUnsavedEdits = false }));
            await editor.RequestCloseAsync(); home.Close();
            Assert.True(home.IsEnabled);
        }
    }

    [AvaloniaFact]
    public async Task SeekBarExistsOnlyInFullscreen()
    {
        var vm = new ProjectWorkspaceViewModel(new Client());
        var editor = new ProjectWorkspaceView { DataContext = vm };
        editor.Show();
        try
        {
            var seek = editor.FindControl<Slider>("FullscreenSeek");
            Assert.NotNull(seek);
            Assert.False(seek.IsVisible);
            editor.SetPreviewFullscreen(true);
            Assert.True(seek.IsVisible);
            editor.SetPreviewFullscreen(false);
            Assert.False(seek.IsVisible);
        }
        finally { await editor.RequestCloseAsync(); }
    }

    private sealed class Client : IProjectClient
    {
        public ProjectSnapshot State { get; set; } = new(Guid.NewGuid(), "Test", "/test", 1, 0,
            ProjectMode.Ready, 0, false, false) { HasUnsavedEdits = true };
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProjectReply(true, null, command == "CloseProject" ? ProjectSnapshot.Closed : State));
    }
}
