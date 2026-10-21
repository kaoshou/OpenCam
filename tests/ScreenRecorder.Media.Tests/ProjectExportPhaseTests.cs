using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public class ProjectExportPhaseTests
{
    [Theory]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.ZhTw, "校驗 GIF")]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.EnUs, "Verifying GIF")]
    public void GifVerificationHasLocalizedLabel(ScreenRecorder.Core.Localization.AppLanguage language, string expected)
    {
        var strings = new ScreenRecorder.Core.Localization.LocalizationService { CurrentLanguage = language };
        Assert.Equal(expected, strings["ProjectExportPhaseVerifyingGif"]);
    }

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
        Assert.Equal(6, labels.Count);
        Assert.Contains(labels, label => label.Contains("GIF"));
        Assert.Equal(4, (int)RecordingExportPhase.Verifying); // Existing IPC values stay stable.
        Assert.Equal(5, (int)RecordingExportPhase.VerifyingGif);
    }
    private sealed class ClosedClient : IProjectClient
    {
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProjectReply(true, null, ProjectSnapshot.Closed));
    }
}
