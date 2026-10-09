using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectExistingExportTests
{
    [Fact]
    public async Task CancelExistingOutputCheckDoesNotExportOrInvalidateProject()
    {
        var client = new WaitingClient();
        var vm = new ProjectWorkspaceViewModel(client);
        vm.ApplyReply(new(true, null, client.State));
        var operation = vm.ExportAsync("unused");
        await client.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            Assert.True(vm.CanCancelExport);
            await vm.CancelExportAsync();
            await operation.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(vm.StatusUnconfirmed);
            Assert.Null(vm.Error);
            Assert.True(vm.CanExport);
            Assert.Equal(0, client.Exports);
        }
        finally { client.Release.TrySetResult(); await operation; }
    }

    private sealed class WaitingClient : IProjectClient
    {
        public ProjectSnapshot State { get; } = new(Guid.NewGuid(), "test", null, 1, 1, ProjectMode.Ready, 1, false, false);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Exports;
        public async Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            if (command == "FindProjectExport") { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            if (command == "ExportRecordingContent") Exports++;
            return new(true, null, State);
        }
    }
}
