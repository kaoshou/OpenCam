// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Projects;
using System.Security.Cryptography;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class EditablePointerSettingsTests
{
    [Fact]
    public async Task SavePreservesThePointerFailureWhenTheSourceCapabilityChanges()
    {
        var client = new PointerSaveFailureClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.PointerStyleIndex = (int)ProjectPointerStyle.Dot;
        await vm.SaveAsync();
        Assert.Equal("Pointer source is no longer available.", vm.Error);
        Assert.Equal(0, client.Saves);
    }

    private sealed class PointerSaveFailureClient : IProjectClient
    {
        private readonly ProjectClip _clip = new() { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Name = "clip", InPts = 0, OutPts = 1000 };
        private ProjectSnapshot _state = new(Guid.NewGuid(), "pointer", "/project", 1, 1, ProjectMode.Ready, 1, false, false)
            { HasEditablePointerSources = true };
        public int Saves;
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            if (command == "SetProjectPointerSettings")
            {
                _state = _state with { HasEditablePointerSources = false, Revision = 2, ClipCount = 0 };
                return Task.FromResult(new ProjectReply(false, "Pointer source is no longer available.", _state));
            }
            if (command == "SaveProject") Saves++;
            if (command == "GetProjectClips") return Task.FromResult(new ProjectReply(true, null, _state, [_clip])
                { TimelineClips = [new(_clip.Id, 0, 10_000_000)] });
            return Task.FromResult(new ProjectReply(true, null, _state));
        }
    }

    [Fact]
    public async Task LegacyEditablePreferenceCannotEnableNewPointerTracksOnEitherPlatform()
    {
        var service = new MockSettingsService();
        service.CurrentSettings.NativeCaptureEnabled = true;
        service.CurrentSettings.EditablePointerEnabled = true;
        var vm = new SettingsViewModel(service, supportsWindowsCapture: true);
        await vm.LoadSettingsAsync();
        Assert.False(vm.CanEnableEditablePointer);
        Assert.False(vm.EditablePointerEnabled);
        vm.EditablePointerEnabled = true; // programmatic/stale input is ignored on save
        await vm.SaveAsync();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(service.CurrentSettings));
        Assert.False(json.RootElement.GetProperty("EditablePointerEnabled").GetBoolean());
        Assert.False(service.CurrentSettings.NativeCaptureEnabled);
        var reloaded = new SettingsViewModel(service, supportsWindowsCapture: true);
        await reloaded.LoadSettingsAsync();
        Assert.False(reloaded.EditablePointerEnabled);
        vm.NativeCaptureEnabled = false;
        Assert.False(vm.EditablePointerEnabled);
        await vm.SaveAsync();
        Assert.False(service.CurrentSettings.EditablePointerEnabled);
    }
}

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task IncompletePointerSourceCanBeExplicitlyHiddenAndSavedForExport()
    {
        await using var scope = new RecordingScope();
        var store = new JsonProjectStore();
        string manifest;
        await using (var handle = await store.CreateAsync(scope.Configuration.OutputDirectory, "Incomplete pointer"))
        {
            var id = Guid.NewGuid();
            var data = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(Path.Combine(handle.ProjectDirectory, "sources", "source.mkv"), data);
            var source = new ProjectSource { Id = id, SessionId = "s", RelativePath = "sources/source.mkv",
                FileSize = data.Length, Sha256 = Convert.ToHexString(SHA256.HashData(data)), Width = 1920, Height = 1080,
                VideoCodec = "h264", Timing = new(new(1, 1000), 0, 1000),
                Pointer = new($"pointers/{id:N}.ocpt", 100, new string('a', 64), 0, false) };
            await handle.SaveAsync(handle.Current with { SchemaVersion = 5, Revision = 1, Sources = [source], Sessions = ["s"],
                Clips = [new() { Id = Guid.NewGuid(), SourceId = id, Name = "retained video", InPts = 0, OutPts = 1000 }] }, 0);
            manifest = Path.Combine(handle.ProjectDirectory, "project.opencam");
        }
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, store, new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        await vm.OpenAsync(manifest);
        Assert.Null(vm.Error);
        Assert.Equal(ProjectPointerStyle.Stick, coordinator.Current!.PointerSettings.Style);
        Assert.True(vm.CanEditPointer);
        // Visible rendering must still require verification; enabling the controls is not a rendering bypass.
        Assert.Throws<InvalidOperationException>(() => ProjectMediaJob.EncodeClipVideo(ProjectRenderPlan.Create(coordinator.Current), 0));
        vm.PointerStyleIndex = (int)ProjectPointerStyle.Hidden;
        await vm.SaveAsync();
        Assert.Null(vm.Error);
        Assert.False(coordinator.IsDirty);
        Assert.Equal(ProjectPointerStyle.Hidden, coordinator.Current.PointerSettings.Style);
        Assert.False(coordinator.Current.Sources[0].Pointer!.Complete);
        Assert.NotNull(ProjectMediaJob.EncodeClipVideo(ProjectRenderPlan.Create(coordinator.Current), 0));
        Assert.True((await coordinator.CloseAsync()).Success);
        Assert.True((await coordinator.OpenAsync(manifest)).Success);
        Assert.Equal(ProjectPointerStyle.Hidden, coordinator.Current!.PointerSettings.Style);
        Assert.False(coordinator.Current.Sources[0].Pointer!.Complete);
    }

    [Fact]
    public async Task PointerEditorChangesFlowThroughIpcUndoDraftAndExplicitSave()
    {
        await using var scope = new RecordingScope();
        var store = new JsonProjectStore();
        string manifest;
        await using (var handle = await store.CreateAsync(scope.Configuration.OutputDirectory, "Pointer editor"))
        {
            var id = Guid.NewGuid();
            var data = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(Path.Combine(handle.ProjectDirectory, "sources", "source.mkv"), data);
            var source = new ProjectSource { Id = id, SessionId = "s", RelativePath = "sources/source.mkv",
                FileSize = data.Length, Sha256 = Convert.ToHexString(SHA256.HashData(data)), Width = 1920, Height = 1080,
                VideoCodec = "h264", Timing = new(new(1, 1000), 0, 1000) };
            using var root = BoundDirectory.Open(handle.ProjectDirectory);
            using var pointers = root.OpenChild("pointers", create: true);
            using (var writer = PointerTrackWriter.Create(pointers, $"{id:N}.ocpt", new(handle.Current.ProjectId, id, 1920, 1080)))
            {
                writer.Append(new(new PointerSample(10, 12, 25, true, 0), null));
                writer.Append(new(null, new NativeFrameClock(0, 10, 9, 1)));
                writer.Seal(new(source.FileSize, source.Sha256));
            }
            using (var file = pointers.Read($"{id:N}.ocpt"))
                source = source with { Pointer = new($"pointers/{id:N}.ocpt", file.Length,
                    Convert.ToHexString(await SHA256.HashDataAsync(file)), 0, true) };
            await handle.SaveAsync(handle.Current with { SchemaVersion = 5, Revision = 1, Sources = [source], Sessions = ["s"],
                Clips = [new() { Id = Guid.NewGuid(), SourceId = id, Name = "clip", InPts = 0, OutPts = 1000 }] }, 0);
            manifest = Path.Combine(handle.ProjectDirectory, "project.opencam");
        }
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, store, new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new()
            { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        await vm.OpenAsync(manifest);
        Assert.Null(vm.Error);
        Assert.True(vm.CanEditPointer);
        var missingInstance = await dispatcher.DispatchAsync(new() { MessageType = "SetProjectPointerSettings",
            PayloadJson = JsonSerializer.Serialize(new ProjectRequest { ProjectId = coordinator.Current!.ProjectId,
                OperationId = Guid.NewGuid(), ExpectedRevision = coordinator.Current.Revision,
                PointerSettings = new(ProjectPointerStyle.Dot, 150) }) });
        Assert.False(missingInstance.Success);
        Assert.Equal(1, coordinator.Current.Revision);
        var revision = coordinator.Current.Revision;
        foreach (var settings in new[] { new ProjectPointerSettings((ProjectPointerStyle)99, 100),
            new(ProjectPointerStyle.Dot, 49), new(ProjectPointerStyle.Arrow, 201) })
        {
            var invalid = await dispatcher.DispatchAsync(new() { MessageType = "SetProjectPointerSettings",
                PayloadJson = JsonSerializer.Serialize(new ProjectRequest { ProjectId = vm.State.ProjectId,
                    ServerInstanceId = vm.State.ServerInstanceId, ExpectedRevision = revision,
                    OperationId = Guid.NewGuid(), PointerSettings = settings }) });
            Assert.False(invalid.Success);
            Assert.Equal(revision, coordinator.Current.Revision);
        }
        vm.PointerStyleIndex = (int)ProjectPointerStyle.Dot; vm.PointerSizePercent = 150;
        Assert.True(vm.HasPointerDraft);
        Assert.False(vm.CanRecord);
        await vm.ApplyPointerSettingsAsync();
        Assert.Null(vm.Error);
        Assert.Equal(new(ProjectPointerStyle.Dot, 150), coordinator.Current!.PointerSettings);
        Assert.True(coordinator.IsDirty); Assert.True(vm.CanUndo);
        vm.PointerSizePercent = 151;
        Assert.False(vm.CanUndo);
        vm.PointerSizePercent = 150;
        await UntilAsync(() => File.Exists(Path.Combine(coordinator.ProjectDirectory!, "project.edits.json")));
        // Independent read of the recovery draft; no manifest save was requested.
        using var draftJson = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(coordinator.ProjectDirectory!, "project.edits.json")));
        var draft = draftJson.RootElement.GetProperty("Draft").Deserialize<ProjectEditDraft>();
        Assert.Equal(new(ProjectPointerStyle.Dot, 150), draft!.PointerSettings);
        await vm.UndoAsync();
        Assert.Equal(new ProjectPointerSettings(), coordinator.Current.PointerSettings);
        await vm.RedoAsync();
        Assert.Equal(new(ProjectPointerStyle.Dot, 150), coordinator.Current.PointerSettings);
        await vm.ResetPointerSettingsAsync();
        Assert.Equal(new ProjectPointerSettings(), coordinator.Current.PointerSettings);
        await vm.UndoAsync();
        Assert.Equal(new(ProjectPointerStyle.Dot, 150), coordinator.Current.PointerSettings);
        foreach (var settings in new[] { new ProjectPointerSettings(ProjectPointerStyle.Hidden, 200), new(ProjectPointerStyle.Arrow, 50) })
        {
            vm.PointerStyleIndex = (int)settings.Style; vm.PointerSizePercent = settings.SizePercent;
            await vm.ApplyPointerSettingsAsync();
            Assert.Equal(settings, coordinator.Current.PointerSettings);
            await vm.UndoAsync();
        }
        await vm.SaveAsync();
        Assert.False(coordinator.IsDirty);
        Assert.True((await coordinator.CloseAsync()).Success);
        Assert.True((await coordinator.OpenAsync(manifest)).Success);
        Assert.Equal(new(ProjectPointerStyle.Dot, 150), coordinator.Current!.PointerSettings);
        var saved = coordinator.Current;
        Assert.False((await coordinator.SetPointerSettingsAsync(new(ProjectPointerStyle.Hidden), saved.Revision - 1)).Success);
        Assert.Equal(saved.PointerSettings, coordinator.Current.PointerSettings);
        Assert.True((await coordinator.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.False((await coordinator.SetPointerSettingsAsync(new(ProjectPointerStyle.Hidden), coordinator.Current.Revision)).Success);
        Assert.Equal(saved.PointerSettings, coordinator.Current.PointerSettings);
        Assert.True((await coordinator.PauseAsync(Guid.NewGuid())).Success);
        Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
    }

    [Fact]
    public async Task PointerCommandRejectsLegacySourcesWithoutChangingProject()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "Legacy pointer");
        Assert.True(ProjectIpcDispatcher.IsProjectCommand("SetProjectPointerSettings"));
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var status = JsonSerializer.Deserialize<ProjectReply>((await dispatcher.DispatchAsync(new() {
            MessageType = "GetProjectStatus", PayloadJson = "{}" })).DataJson!)!;
        var response = await dispatcher.DispatchAsync(new() { MessageType = "SetProjectPointerSettings",
            PayloadJson = JsonSerializer.Serialize(new { ProjectId = coordinator.Current!.ProjectId,
                ServerInstanceId = status.State.ServerInstanceId,
                OperationId = Guid.NewGuid(), ExpectedRevision = coordinator.Current.Revision,
                PointerSettings = new ProjectPointerSettings(ProjectPointerStyle.Dot, 150) }) });
        Assert.False(response.Success);
        Assert.Contains("pointer", response.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, coordinator.Current!.Revision);
        Assert.False(coordinator.CanUndo);
    }
}
