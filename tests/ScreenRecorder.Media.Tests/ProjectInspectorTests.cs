// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using System.Collections.Specialized;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectInspectorTests
{
    [AvaloniaFact]
    public async Task SavingInspectorKeepsTypingEnabledAndPreservesFocusCaretAndNewerText()
    {
        var client = new InspectorClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        var window = new ProjectWorkspaceView { DataContext = vm, Width = 1440, Height = 900 };
        var release = new TaskCompletionSource();
        Task saving = Task.CompletedTask;
        try
        {
            window.Show(); window.UpdateLayout();
            if (!window.FindControl<Border>("InspectorPane")!.IsVisible)
                window.FindControl<Button>("PropertiesToggle")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            var input = window.FindControl<Border>("InspectorPane")!.GetVisualDescendants()
                .OfType<TextBox>().Single(t => t.MaxLength == 200);
            Assert.True(input.IsEffectivelyVisible, $"Inspector visibility at {window.ClientSize}");
            Assert.True(input.IsEffectivelyEnabled);
            Assert.True(input.Focus(), $"Focusable={input.Focusable}, visible={input.IsEffectivelyVisible}");
            Assert.True(input.IsFocused);
            input.Text = "First"; input.CaretIndex = 5;
            client.EditRelease = release;
            saving = vm.ApplyPropertiesAsync();
            Assert.True(vm.IsBusy);
            Assert.True(input.IsEffectivelyEnabled);
            Assert.True(input.IsFocused);
            input.Text = "First still typing"; input.CaretIndex = 7;
            release.SetResult(); await saving;
            Assert.Equal("First still typing", input.Text);
            Assert.Equal(7, input.CaretIndex);
            Assert.True(input.IsFocused);
            await WaitForAsync(() => !vm.HasPropertyDraft && !vm.IsBusy);
            Assert.Equal("First still typing", client.Clip.Name);
            Assert.True(input.IsFocused);
            Assert.Equal(7, input.CaretIndex);
        }
        finally { release.TrySetResult(); await saving; await window.RequestCloseAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PropertySaveDoesNotBlankPreviewOrResetClipList(bool effect)
    {
        var client = new InspectorClient(); var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync(); await vm.PollPreviewAsync();
        var before = vm.PreviewFrame; Assert.NotNull(before);
        var resets = 0; vm.Clips.CollectionChanged += (_, e) => { if(e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        if (effect) vm.ClipScalePercent = 125; else vm.ClipName = "Renamed";
        await vm.ApplyPropertiesAsync();
        Assert.Same(before, vm.PreviewFrame);
        Assert.Equal(0, resets);
        await vm.PollPreviewAsync();
        if(effect) Assert.Equal(vm.State.Revision, vm.PreviewFrame!.Revision);
        else Assert.Equal(1, client.FrameRequests);
    }

    [Fact]
    public async Task RejectedPropertySaveRetainsDraftAndAllowsCorrection()
    {
        var client = new InspectorClient { RejectNextEdit = true };
        var vm = new ProjectWorkspaceViewModel(client); await vm.RefreshAsync();
        vm.ClipName = "Keep this draft"; await vm.ApplyPropertiesAsync();
        Assert.Equal("Keep this draft", vm.ClipName);
        Assert.True(vm.HasPropertyDraft); Assert.True(vm.CanEditProperties);
        Assert.Equal("Original", client.Clip.Name); Assert.NotNull(vm.Error);
        vm.ClipName = "Corrected";
        await WaitForAsync(() => !vm.IsBusy && !vm.HasPropertyDraft);
        Assert.Equal("Corrected", client.Clip.Name); Assert.Null(vm.Error);
    }

    [Fact]
    public async Task PropertyChangesAutomaticallyApplyAsOneEditingBurst()
    {
        var client = new InspectorClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.ClipName = "Automatic";
        vm.ClipVolumePercent = 65;
        vm.ClipMuted = true;
        vm.ClipFadeInSeconds = .1m;
        vm.ClipScalePercent = 120;
        await WaitForAsync(() => !vm.HasPropertyDraft && client.Edits.Count == 1);
        var edit = Assert.IsType<ProjectClipEdit.Properties>(Assert.Single(client.Edits));
        Assert.Equal("Automatic", edit.Name);
        Assert.Equal(.65, edit.Volume);
        Assert.True(edit.Muted);
        Assert.Equal(1_000_000, edit.FadeInTicks);
        Assert.Equal(1.2, edit.Scale);
        await Task.Delay(350);
        Assert.Single(client.Edits);
    }

    [Fact]
    public async Task InvalidAutomaticInputCanBeCorrectedWithoutAnApplyButton()
    {
        var client = new InspectorClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.ClipFadeInSeconds = null;
        await WaitForAsync(() => vm.Error is not null);
        Assert.Empty(client.Edits);
        Assert.False(vm.StatusUnconfirmed);
        vm.ClipFadeInSeconds = .25m;
        await WaitForAsync(() => !vm.HasPropertyDraft && client.Edits.Count == 1);
        Assert.Null(vm.Error);
        Assert.Equal(2_500_000, client.Clip.FadeInTicks);
    }

    [Fact]
    public async Task ReloadAndDiscardCancelPendingAutomaticChanges()
    {
        var client = new InspectorClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        vm.ClipName = "Discard";
        vm.ResetProperties();
        await Task.Delay(400);
        Assert.Empty(client.Edits);
        Assert.False(vm.HasPropertyDraft);
        vm.ClipVolumePercent = 60;
        await vm.SaveAsync();
        await Task.Delay(400);
        Assert.Single(client.Edits);
        Assert.Equal(new[] { "ApplyProjectEdit", "SaveProject" }, client.Mutations);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, deadline.Token);
    }

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
        public TaskCompletionSource? EditRelease;
        public int FrameRequests;
        public bool RejectNextEdit;
        public async Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            if(command == "GetProjectFrame")
            {
                FrameRequests++;
                return new(true, null, State) { Frame = new(State.Revision, request.TimelineTicks, Clip.Id, new byte[ProjectFrameReply.ByteCount]) };
            }
            if (command is "ApplyProjectEdit" or "SaveProject") Mutations.Add(command);
            if (request.Edit is { } edit)
            {
                if (RejectNextEdit) { RejectNextEdit = false; return new(false, "Save rejected", State); }
                var release = EditRelease; EditRelease = null;
                if(release is not null) await release.Task;
                Edits.Add(edit);
                if (edit is ProjectClipEdit.Properties p)
                {
                    Clip = Clip with { Name = p.Name, Volume = p.Volume, Muted = p.Muted,
                        FadeInTicks = p.FadeInTicks, FadeOutTicks = p.FadeOutTicks, Scale = p.Scale,
                        Crop = p.Crop, PositionX = p.PositionX, PositionY = p.PositionY };
                    State = State with { Revision = State.Revision + 1 };
                }
            }
            return new ProjectReply(true, null, State, [Clip]) {
                TimelineClips = [new(Clip.Id, 0, 20_000_000)] };
        }
    }
}
