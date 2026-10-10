using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public class ProjectExportPhaseTests
{
    [Fact]
    public void RunningStatusDistinguishesCopyAudioConversionAndRender()
    {
        var vm = new ProjectWorkspaceViewModel(new ClosedClient());
        var labels = new HashSet<string>();
        foreach (var phase in Enum.GetValues<RecordingExportPhase>())
        {
            vm.ApplyReply(new ProjectReply(true, null, ProjectSnapshot.Closed with {
                Export = new(Guid.NewGuid(), 0, RecordingExportState.Running, .5) { Phase = phase } }));
            labels.Add(vm.ExportStatusText);
            Assert.Contains("50%", vm.ExportStatusText);
        }
        Assert.Equal(5, labels.Count);
    }
    private sealed class ClosedClient : IProjectClient
    {
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProjectReply(true, null, ProjectSnapshot.Closed));
    }
}
