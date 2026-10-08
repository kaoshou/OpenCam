// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectInspectorTests
{
    [Fact]
    public async Task DraftMustBeAppliedOrDiscardedBeforeOtherTimelineEdits()
    {
        var client = new InspectorClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.ClipMuted = true;
        Assert.False(vm.CanEditTimeline);
        vm.Seek(10_000_000);
        await vm.DeleteSelectedAsync();
        Assert.Equal(0, vm.PlayheadTicks);
        Assert.Empty(client.Edits);
        vm.ResetProperties();
        Assert.True(vm.CanEditTimeline);
        vm.Seek(10_000_000);
        Assert.Equal(10_000_000, vm.PlayheadTicks);
    }

    [Fact]
    public async Task NonFiniteDraftBlocksSaveAndClose()
    {
        var client = new InspectorClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.ClipVolumePercent = double.NaN;
        Assert.True(vm.HasPropertyDraft);
        await vm.SaveAsync();
        Assert.False(await vm.CloseAsync());
        Assert.Empty(client.Mutations);
        Assert.False(vm.StatusUnconfirmed);
    }

    [Fact]
    public async Task SaveAppliesValidDraftFirstButDoesNotSaveInvalidDraft()
    {
        var client = new InspectorClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.ClipVolumePercent = 60;
        Assert.True(vm.HasPropertyDraft);
        await vm.SaveAsync();
        Assert.Equal(new[] { "ApplyProjectEdit", "SaveProject" }, client.Mutations);
        Assert.Equal(.6, vm.SelectedClip!.Volume);
        Assert.False(vm.HasPropertyDraft);
        client.Mutations.Clear();
        vm.ClipFadeInSeconds = 10;
        await vm.SaveAsync();
        Assert.Empty(client.Mutations);
        Assert.True(vm.HasPropertyDraft);
    }

    [Fact]
    public async Task DraftDoesNotMutateClipAndAppliesAsSingleCommand()
    {
        var client = new InspectorClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.ClipName = "Edited";
        vm.ClipVolumePercent = 75;
        vm.ClipMuted = true;
        vm.ClipFadeInSeconds = .125m;
        vm.ClipFadeOutSeconds = .25m;
        vm.ClipScalePercent = 120;
        vm.ClipCropPercent = 5;
        vm.ClipPositionX = -10;
        Assert.Equal(1, vm.SelectedClip!.Volume);
        Assert.Empty(client.Edits);
        await vm.ApplyPropertiesAsync();
        var edit = Assert.IsType<ProjectClipEdit.Properties>(Assert.Single(client.Edits));
        Assert.Equal(client.Clip.Id, edit.ClipId);
        Assert.Equal("Edited", edit.Name);
        Assert.Equal(.75, edit.Volume);
        Assert.True(edit.Muted);
        Assert.Equal(1_250_000, edit.FadeInTicks);
        Assert.Equal(2_500_000, edit.FadeOutTicks);
        Assert.Equal(1.2, edit.Scale);
        Assert.Equal(5, edit.Crop);
        Assert.Equal(-10, edit.PositionX);
    }

    [Fact]
    public async Task CancelDraftRestoresSelectionAndInvalidFadeNeverSends()
    {
        var client = new InspectorClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.ClipVolumePercent = 35;
        vm.ClipName = "Unsaved";
        vm.ResetProperties();
        Assert.Equal(100, vm.ClipVolumePercent);
        Assert.Equal(client.Clip.Name, vm.ClipName);
        vm.ClipFadeInSeconds = null;
        await vm.ApplyPropertiesAsync();
        Assert.Empty(client.Edits);
        Assert.NotNull(vm.Error);
        Assert.False(vm.StatusUnconfirmed);
        vm.ClipFadeInSeconds = 3;
        await vm.ApplyPropertiesAsync();
        Assert.Empty(client.Edits);
        vm.ClipFadeInSeconds = 0;
        vm.ApplyReply(new(true, null, client.State with { Mode = ProjectMode.Recording }));
        await vm.ApplyPropertiesAsync();
        Assert.Empty(client.Edits);
    }

    private sealed class InspectorClient : IProjectClient
    {
        public ProjectClip Clip = new() { Id = Guid.NewGuid(), Name = "Original", InPts = 5000, OutPts = 7000 };
        public ProjectSnapshot State = new(Guid.NewGuid(), "Test", "/test", 0, 0, ProjectMode.Ready, 1, false, false);
        public List<ProjectClipEdit> Edits = [];
        public List<string> Mutations = [];
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            if (command is "ApplyProjectEdit" or "SaveProject") Mutations.Add(command);
            if (request.Edit is { } edit)
            {
                Edits.Add(edit);
                if (edit is ProjectClipEdit.Properties p)
                {
                    Clip = Clip with { Name = p.Name, Volume = p.Volume, Muted = p.Muted,
                        FadeInTicks = p.FadeInTicks, FadeOutTicks = p.FadeOutTicks, Scale = p.Scale,
                        Crop = p.Crop, PositionX = p.PositionX, PositionY = p.PositionY };
                    State = State with { Revision = State.Revision + 1 };
                }
            }
            return Task.FromResult(new ProjectReply(true, null, State, [Clip]) {
                TimelineClips = [new(Clip.Id, 0, 20_000_000)] });
        }
    }
}
