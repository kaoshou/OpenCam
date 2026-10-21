// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectGifExportDialogTests
{
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(0,0,480,10)] [InlineData(0,1,480,15)] [InlineData(0,2,480,20)]
    [InlineData(1,0,640,10)] [InlineData(1,1,640,15)] [InlineData(1,2,640,20)]
    [InlineData(2,0,960,10)] [InlineData(2,1,960,15)] [InlineData(2,2,960,20)]
    [InlineData(3,0,null,10)] [InlineData(3,1,null,15)] [InlineData(3,2,null,20)]
    public async Task ChosenSettingsAreUsedForExport(int widthIndex, int fpsIndex, int? widthValue, int fpsValue)
    {
        var owner = new Avalonia.Controls.Window();
        var dialog = new ProjectGifExportDialog(new(Guid.NewGuid(), 7, null, "短片", TimeSpan.FromSeconds(1).Ticks));
        try
        {
            owner.Show(); var result = dialog.ShowDialog<GifExportOptions?>(owner); dialog.UpdateLayout();
            var controls = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).ToArray();
            controls.OfType<Avalonia.Controls.ComboBox>().Single(c => c.Name == "GifWidth").SelectedIndex = widthIndex;
            controls.OfType<Avalonia.Controls.ComboBox>().Single(c => c.Name == "GifFps").SelectedIndex = fpsIndex;
            controls.OfType<Avalonia.Controls.Button>().Single(c => c.Name == "GifConfirm")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Equal(new GifExportOptions(widthValue,fpsValue), await result);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(299.999, false, ScreenRecorder.Core.Localization.AppLanguage.ZhTw, "GIF 不包含音訊。")]
    [InlineData(300, true, ScreenRecorder.Core.Localization.AppLanguage.ZhTw, "GIF 不包含音訊。")]
    [InlineData(300.001, true, ScreenRecorder.Core.Localization.AppLanguage.EnUs, "GIF does not include audio.")]
    [InlineData(600, true, ScreenRecorder.Core.Localization.AppLanguage.EnUs, "GIF does not include audio.")]
    public async Task SettingsRequireLongDurationConfirmationButNeverImposeDurationCap(double seconds, bool warn,
        ScreenRecorder.Core.Localization.AppLanguage language, string notice)
    {
        var previous = ScreenRecorder.UI.Localization.LanguageManager.Instance.CurrentLanguage;
        ScreenRecorder.UI.Localization.LanguageManager.Instance.CurrentLanguage = language;
        var owner = new Avalonia.Controls.Window();
        var dialog = new ProjectGifExportDialog(new(Guid.NewGuid(), 7, null, "影片", TimeSpan.FromSeconds(seconds).Ticks));
        try
        {
            owner.Show(); var result = dialog.ShowDialog<GifExportOptions?>(owner); dialog.UpdateLayout();
            var controls = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).ToArray();
            Assert.Contains(controls.OfType<Avalonia.Controls.TextBlock>(), b => b.Text == notice);
            var width = controls.OfType<Avalonia.Controls.ComboBox>().Single(c => c.Name == "GifWidth");
            var fps = controls.OfType<Avalonia.Controls.ComboBox>().Single(c => c.Name == "GifFps");
            Assert.Equal(1, width.SelectedIndex); Assert.Equal(1, fps.SelectedIndex);
            Assert.Equal(4, width.ItemCount); Assert.Equal(3, fps.ItemCount);
            var confirm = controls.OfType<Avalonia.Controls.Button>().Single(c => c.Name == "GifConfirm");
            confirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            if (warn)
            {
                Assert.False(result.IsCompleted);
                Assert.True(controls.OfType<Avalonia.Controls.TextBlock>().Single(c => c.Name == "GifWarning").IsVisible);
                confirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            }
            Assert.Equal(new GifExportOptions(640,15), await result);
        }
        finally { dialog.Close(); owner.Close(); ScreenRecorder.UI.Localization.LanguageManager.Instance.CurrentLanguage = previous; }
    }

    [Fact]
    public async Task PreviewShutdownCannotReplaceTheCapturedExportRevision()
    {
        var client = new ExportClient(); var vm = new ProjectWorkspaceViewModel(client); await vm.RefreshAsync();
        var target = (await vm.PrepareExportAsync(null))!;
        await vm.TogglePlaybackAsync(); Assert.True(vm.IsPlayingPreview);
        client.BeforeSend = command => { if (command == "StopProjectPreview") vm.State = vm.State with { Revision=8,SavedRevision=8 }; };
        await vm.ExportFileAsync("/test/export.gif",target,new(ProjectExportFormat.Gif,Gif:new()));
        Assert.Empty(client.Exports);
    }

    [Fact]
    public async Task CapturesSelectedClipDurationAndRoutesOptionsWithoutChangingProject()
    {
        var client = new ExportClient(); var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        await vm.RequestExportAsync(ProjectExportFormat.Gif, client.Clips[1].Id, target => {
            Assert.Equal(TimeSpan.FromSeconds(3).Ticks, target.DurationTicks);
            Assert.Equal("片段 / name", target.Name);
            return Task.FromResult<GifExportOptions?>(new(480, 20));
        }, (_, format) => { Assert.Equal(ProjectExportFormat.Gif, format); return Task.FromResult<string?>("/test/clip.gif"); });
        var request = Assert.Single(client.Exports);
        Assert.Equal(new ProjectExportOptions(ProjectExportFormat.Gif, ProjectExportScope.Clip, client.Clips[1].Id, new(480,20)), request.ExportOptions);
        Assert.Equal(client.State.ProjectId, request.ProjectId);
        Assert.Equal(7, request.ExpectedRevision);
        Assert.True(request.PathIsExportFile);
    }

    [Theory]
    [InlineData("options")]
    [InlineData("destination")]
    [InlineData("revision")]
    [InlineData("project")]
    [InlineData("deleted")]
    [InlineData("recording")]
    [InlineData("unknown")]
    [InlineData("busy")]
    public async Task CancelOrChangedTargetCannotDispatchExport(string change)
    {
        var client = new ExportClient(); var vm = new ProjectWorkspaceViewModel(client); await vm.RefreshAsync();
        var prepared = false;
        await vm.RequestExportAsync(ProjectExportFormat.Gif, client.Clips[1].Id,
            _ => { prepared = true; return Task.FromResult<GifExportOptions?>(change == "options" ? null : new()); },
            (_, _) => {
                if (change == "revision") vm.State = vm.State with { Revision = 8, SavedRevision = 8 };
                if (change == "project") vm.State = vm.State with { ProjectId = Guid.NewGuid() };
                if (change == "deleted") vm.Clips.RemoveAt(1);
                if (change == "recording") vm.State = vm.State with { Mode = ProjectMode.Recording };
                if (change == "unknown") vm.StatusUnconfirmed = true;
                if (change == "busy") vm.IsBusy = true;
                return Task.FromResult<string?>(change == "destination" ? null : "/test/clip.gif");
            });
        Assert.True(prepared); Assert.Empty(client.Exports);
    }

    [Fact]
    public async Task Mp4SkipsGifOptionsAndDuplicateSelectionDoesNotOpenSecondDialog()
    {
        var client = new ExportClient(); var vm = new ProjectWorkspaceViewModel(client); await vm.RefreshAsync();
        var pick = new TaskCompletionSource<string?>(); var calls = 0;
        Task<GifExportOptions?> Gif(ProjectExportTarget _) => throw new Exception("MP4 must not ask for GIF options");
        Task<string?> Destination(ProjectExportTarget _, ProjectExportFormat __) { calls++; return pick.Task; }
        var first = vm.RequestExportAsync(ProjectExportFormat.Mp4, null, Gif, Destination);
        await vm.RequestExportAsync(ProjectExportFormat.Mp4, null, Gif, Destination);
        pick.SetResult("/test/video.mp4"); await first;
        Assert.Equal(1, calls); Assert.Single(client.Exports);
        Assert.Equal(ProjectExportScope.WholeProject, client.Exports[0].ExportOptions!.Scope);
    }

    internal sealed class ExportClient : IProjectClient
    {
        public ProjectSnapshot State = new(Guid.NewGuid(), "Long movie", "/test", 7, 7, ProjectMode.Ready, 2, false, false);
        public ProjectClip[] Clips = [new() { Id=Guid.NewGuid(), Name="First" }, new() { Id=Guid.NewGuid(), Name="片段 / name" }];
        public List<ProjectRequest> Exports = [];
        public Action<string>? BeforeSend;
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            BeforeSend?.Invoke(command);
            if (command == "ExportRecordingContent") Exports.Add(request);
            return Task.FromResult(new ProjectReply(true, null, State, command == "GetProjectClips" ? Clips : null) {
                Playback = command == "PlayProjectPreview" ? new(Guid.NewGuid(),0,true,null) : null,
                TimelineClips = command == "GetProjectClips" ? [new(Clips[0].Id,0,TimeSpan.FromSeconds(600).Ticks),
                    new(Clips[1].Id,TimeSpan.FromSeconds(600).Ticks,TimeSpan.FromSeconds(603).Ticks)] : null });
        }
    }
}
