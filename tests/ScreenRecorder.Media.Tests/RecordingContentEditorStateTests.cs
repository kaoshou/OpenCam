// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class RecordingContentEditorStateTests
{
    [Fact]
    public void SavedContentCannotBeEditedOrResumedUntilExportReleasesItsSnapshot()
    {
        var vm = new ProjectWorkspaceViewModel(new UnusedClient());
        var state = new ProjectSnapshot(Guid.NewGuid(), "Recorded content", null, 4, 4,
            ProjectMode.Ready, 1, true, true)
        { Export = new(Guid.NewGuid(), 4, RecordingExportState.Running, 0.25) };
        vm.ApplyReply(new(true, null, state));
        Assert.False(vm.CanEdit);
        Assert.False(vm.CanRecord);
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
        Assert.False(vm.CanClose);
        foreach (var terminal in new[] { RecordingExportState.Failed, RecordingExportState.Canceled, RecordingExportState.Succeeded })
        {
            vm.ApplyReply(new(true, null, state with { Export = state.Export with { State = terminal } }));
            Assert.True(vm.CanEdit);
            Assert.True(vm.CanRecord);
            Assert.True(vm.CanClose);
            Assert.False(vm.State.IsDirty);
        }
    }

    private sealed class UnusedClient : IProjectClient
    {
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("This state transition must not issue a command.");
    }
}
