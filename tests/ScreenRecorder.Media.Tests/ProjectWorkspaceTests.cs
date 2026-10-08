// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectWorkspaceTests
{
    [Fact]
    public async Task BackgroundPollShowsSafetyStopWithoutAnotherUserAction()
    {
        var client = new FakeClient();
        var vm = new ProjectWorkspaceViewModel(client);
        vm.ApplyReply(new(true, null, client.State with { Mode = ProjectMode.Recording }));
        client.State = client.State with { Mode = ProjectMode.Ready, LastError = "Capture device lost" };
        client.Clips = [new ProjectClip { Id = Guid.NewGuid(), Name = "Saved source" }];
        await vm.PollRecordingAsync();
        Assert.Equal(ProjectMode.Ready, vm.State.Mode);
        Assert.Equal("Capture device lost", vm.Error);
        Assert.False(vm.CanPause);
        Assert.Equal("Saved source", Assert.Single(vm.Clips).Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SaveShortcutWorksInsideTextInput_WhileUndoRemainsTextLocal(bool mac)
    {
        var modifier = mac ? Avalonia.Input.KeyModifiers.Meta : Avalonia.Input.KeyModifiers.Control;
        Assert.Equal("save", ProjectWorkspaceView.ResolveShortcut(Avalonia.Input.Key.S, modifier, true, mac));
        Assert.Null(ProjectWorkspaceView.ResolveShortcut(Avalonia.Input.Key.Z, modifier, true, mac));
        Assert.Equal("undo", ProjectWorkspaceView.ResolveShortcut(Avalonia.Input.Key.Z, modifier, false, mac));
    }

    [Fact]
    public async Task SaveAndRenameUseProjectCommandsOnly_RecordingCloseDoesNotStop()
    {
        var client = new FakeClient();
        var vm = new ProjectWorkspaceViewModel(client);
        vm.ApplyReply(new(true, null, client.State));
        await vm.RenameAsync(Guid.NewGuid(), "New name");
        await vm.SaveAsync();
        Assert.Contains("RenameProjectClip", client.Commands);
        Assert.Contains("SaveProject", client.Commands);
        vm.ApplyReply(new(true, null, client.State with { Mode = ProjectMode.Recording }));
        Assert.False(await vm.CloseAsync());
        Assert.DoesNotContain("CloseProject", client.Commands);
        Assert.DoesNotContain(client.Commands, x => x is "StopRecording" or "FinishProjectRecording" or "Export");
    }

    [Fact]
    public void OlderSaveReplyCannotClearNewerDirtyRevision()
    {
        var client = new FakeClient();
        var vm = new ProjectWorkspaceViewModel(client);
        vm.ApplyReply(new(false, "disk full", client.State with { Revision = 4, SavedRevision = 2 }));
        vm.ApplyReply(new(true, null, client.State with { Revision = 3, SavedRevision = 3 }));
        Assert.Equal(4, vm.State.Revision);
        Assert.True(vm.State.IsDirty);
    }

    [Fact]
    public async Task UnknownResponseBlocksEditingAndClose_UntilStatusConfirmed()
    {
        var client = new FakeClient { Unknown = true };
        var vm = new ProjectWorkspaceViewModel(client);
        vm.ApplyReply(new(true, null, client.State));
        await vm.SaveAsync();
        Assert.True(vm.StatusUnconfirmed);
        Assert.False(vm.CanEdit);
        Assert.False(await vm.CloseAsync());
        client.Unknown = false;
        await vm.RefreshAsync();
        Assert.False(vm.StatusUnconfirmed);
        Assert.True(vm.CanEdit);
    }

    [Fact]
    public async Task CloseWaitsForSaveAndStaysOpenWhenSaveFails()
    {
        var client = new FakeClient();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnSend = async command => { if (command == "SaveProject") { entered.SetResult(); await release.Task; } };
        var vm = new ProjectWorkspaceViewModel(client);
        vm.ApplyReply(new(true, null, client.State));
        var save = vm.SaveAsync();
        await entered.Task;
        var close = vm.CloseAsync();
        Assert.False(close.IsCompleted);
        client.Fail = true;
        release.SetResult();
        await save;
        Assert.False(await close);
        Assert.NotNull(vm.State.ProjectId);
    }

    private sealed class FakeClient : IProjectClient
    {
        public ProjectSnapshot State = new(Guid.NewGuid(), "Lesson", "/test", 1, 1, ProjectMode.Ready, 0, true, false);
        public bool Unknown, Fail;
        public List<string> Commands = [];
        public ProjectClip[] Clips = [];
        public Func<string, Task>? OnSend;
        public async Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            Commands.Add(command);
            if (OnSend is not null) await OnSend(command);
            if (Unknown) return new(false, "unconfirmed", State, Unconfirmed: true, OperationKnown: false);
            if (Fail) return new(false, "disk full", State with { Revision = 2, SavedRevision = 1 });
            return new(true, null, State, Clips);
        }
    }
}
