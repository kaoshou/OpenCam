// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Mp4ProfileRoutesToCorrectBackend(bool compressed, bool clip, bool web)
    {
        await using var scope = new RecordingScope();
        var original = new ControlledContentExporter();
        var configured = new ControlledContentExporter();
        Mp4ExportOptions? received = null;
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(),
            new ProjectProbeStub(), original, mp4Exporter: options => { received = options; return configured; });
        Assert.True((await coordinator.StartNewContentAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
        var snapshot = coordinator.Current!;
        var profile = new Mp4ExportOptions(compressed ? Mp4ExportQuality.Compact : Mp4ExportQuality.Original,
            compressed ? Mp4ExportResolution.Hd : Mp4ExportResolution.Original);
        var options = new ProjectExportOptions(Scope: clip ? ProjectExportScope.Clip : ProjectExportScope.WholeProject,
            ClipId: clip ? snapshot.Clips[0].Id : null, Mp4: profile);
        var result = await coordinator.ExportAsync(scope.Configuration.OutputDirectory, snapshot.Revision,
            Guid.NewGuid(), webPlayer: web, exportOptions: options);
        Assert.True(result.Success, result.ErrorCode);
        var selected = compressed ? configured : original;
        await selected.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(compressed ? profile : null, received);
        Assert.Equal(0, (compressed ? original : configured).Calls);
        Assert.Equal(snapshot.Clips[0].Id, Assert.Single(selected.Snapshot!.Clips).Id);
        await coordinator.CancelExportAsync(coordinator.ExportStatus!.ExportId);
        await UntilAsync(() => coordinator.ExportStatus?.State == RecordingExportState.Canceled);
    }
}
