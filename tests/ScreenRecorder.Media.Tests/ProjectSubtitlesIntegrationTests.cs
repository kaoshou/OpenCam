// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task ChangedTimelineBlocksWebExportUntilSubtitlesAreConfirmed()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        await project.CreateAsync(scope.Configuration.OutputDirectory, "字幕同步");
        var file = Path.Combine(scope.Configuration.OutputDirectory, "a.srt");
        await File.WriteAllTextAsync(file, "1\n00:00:01,000 --> 00:00:02,000\n教材");
        var track = await project.ImportSubtitleAsync(file, "zh-TW", 0);
        await project.SetSubtitlesAsync(new([track], track.Id), 0);
        await project.SaveAsync(project.Current!.Revision);
        await project.StartAsync(scope.Configuration, Guid.NewGuid());
        await project.PauseAsync(Guid.NewGuid());
        Assert.True(project.SubtitlesNeedSyncConfirmation);
        var result = await project.ExportAsync(scope.Configuration.OutputDirectory, project.Current!.Revision, Guid.NewGuid(), webPlayer: true);
        Assert.False(result.Success);
        Assert.Contains("subtitle", result.ErrorCode!, StringComparison.OrdinalIgnoreCase);
        Assert.True((await project.SetSubtitlesAsync(project.Current!.Subtitles, project.Current.Revision, confirmSync: true)).Success);
        Assert.False(project.SubtitlesNeedSyncConfirmation);
        Assert.True(project.IsDirty);
    }

    [Fact]
    public async Task EditorSubtitleSelectionUsesPlayheadWithoutChangingProjectDefault()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        await project.CreateAsync(scope.Configuration.OutputDirectory, "字幕");
        var file = Path.Combine(scope.Configuration.OutputDirectory, "zh-TW.srt");
        await File.WriteAllTextAsync(file, "1\n00:00:01,000 --> 00:00:02,000\n教材");
        var track = await project.ImportSubtitleAsync(file, "zh-TW", 0);
        Assert.True((await project.SetSubtitlesAsync(new([track], track.Id), 0)).Success);
        var dispatcher = new ProjectIpcDispatcher(project);
        var client = new ScreenRecorder.UI.Projects.ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) }),
            (request, ct) => dispatcher.DispatchMediaAsync(new() { MessageType = "GetProjectFrame", PayloadJson = JsonSerializer.Serialize(request) }));
        var vm = new ScreenRecorder.UI.Projects.ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        await vm.SelectPreviewSubtitleAsync(track.Id);
        vm.PlayheadTicks = 15000000;
        Assert.Equal("教材", vm.PreviewSubtitleText);
        vm.PlayheadTicks = 20000000;
        Assert.Equal("", vm.PreviewSubtitleText);
        vm.PlayheadTicks = 15000000;
        await vm.SelectPreviewSubtitleAsync(null);
        Assert.Equal("", vm.PreviewSubtitleText);
        Assert.Equal(track.Id, project.Current!.Subtitles.DefaultTrackId);
    }

    [Fact]
    public async Task SubtitleImportApplyPreviewAndSaveUseOneProjectWithoutSilentSave()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        Assert.True((await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "字幕教材")).Success);
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        async Task<ProjectReply> Send(string command, ProjectRequest request)
        {
            var response = await dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) });
            return JsonSerializer.Deserialize<ProjectReply>(response.DataJson!)!;
        }
        var status = await Send("GetProjectStatus", new());
        var path = Path.Combine(scope.Configuration.OutputDirectory, "a.srt");
        await File.WriteAllTextAsync(path, "1\n00:00:01,000 --> 00:00:02,000\n教材");
        var importRequest = new ProjectRequest { ProjectId = status.State.ProjectId, ServerInstanceId = status.State.ServerInstanceId,
            OperationId = Guid.NewGuid(), ExpectedRevision = 0, Path = path, SubtitleLanguage = "zh-TW" };
        var imported = await Send("ImportProjectSubtitle", importRequest);
        Assert.True(imported.Success, imported.Error);
        var track = Assert.IsType<ProjectSubtitleTrack>(imported.ImportedSubtitle);
        Assert.False(coordinator.IsDirty);
        var replay = await Send("ImportProjectSubtitle", importRequest);
        Assert.Equal(track, replay.ImportedSubtitle);
        var applied = await Send("EditProjectSubtitles", importRequest with { OperationId = Guid.NewGuid(),
            SubtitleSettings = new([track], track.Id) });
        Assert.True(applied.Success, applied.Error);
        Assert.True(coordinator.IsDirty);
        var media = await dispatcher.DispatchMediaAsync(new() { MessageType = "GetProjectFrame", PayloadJson = JsonSerializer.Serialize(
            new ProjectRequest { ProjectId = applied.State.ProjectId, ServerInstanceId = applied.State.ServerInstanceId,
                ExpectedRevision = applied.State.Revision, SubtitleTrackId = track.Id }) });
        Assert.True(media.Success, media.Error);
        Assert.Equal("教材", Assert.Single(JsonSerializer.Deserialize<ProjectSubtitleCue[]>(media.SubtitleData!)!).Text);
        Assert.True((await coordinator.SaveAsync(coordinator.Current!.Revision)).Success);
        Assert.False(coordinator.IsDirty);
        Assert.Equal(4, coordinator.Current.SchemaVersion);
    }
}
