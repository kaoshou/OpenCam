using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.IPC;
using System.Text.Json;

namespace ScreenRecorder.Media.Tests;
public sealed class ProjectScopedExportTests
{
    [MacOsOnlyTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task GifExportVerifiesBeforePublishingAndCleansFailedStages(int failAt)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-gif-export-");
        try
        {
            await using var owner = await RecordingContentExportIntegrationTests.SingleClip(root.FullName, audio: true);
            var source = owner.Current.Sources[0];
            var output = Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName;
            var sentinel = Path.Combine(output, "old.gif");
            await File.WriteAllTextAsync(sentinel, "keep");
            var process = new FailProcess(new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!), failAt);
            var exporter = new ProjectGifExporter(process, new());
            if (failAt != 0)
            {
                await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(owner, owner.Current, output,
                    Guid.NewGuid(), new Progress<double>(), default));
                Assert.Single(Directory.GetFiles(output));
            }
            else
            {
                var progress = new PhaseProgress();
                var result = await exporter.ExportAsync(owner, owner.Current, output, Guid.NewGuid(), progress, default);
                Assert.Contains(progress.Phases, phase => phase.ToString() == "VerifyingGif");
                Assert.DoesNotContain(RecordingExportPhase.Verifying, progress.Phases);
                Assert.True(result.Success, result.Error);
                Assert.EndsWith(".gif", result.FinalPath);
                using var gif = File.OpenRead(result.FinalPath!);
                var info = GifStructureReader.Read(gif);
                Assert.Equal(30, info.DurationCentiseconds);
                Assert.Equal(5, info.FrameCount);
                Assert.True(info.InfiniteLoop);
                Assert.Equal(2, Directory.GetFiles(output).Length);
            }
            Assert.Equal("keep", await File.ReadAllTextAsync(sentinel));
            Assert.Equal(source.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
                Path.Combine(owner.ProjectDirectory, source.RelativePath)))));
        }
        finally { root.Delete(true); }
    }

    private sealed class PhaseProgress : IRecordingExportProgress
    {
        public List<RecordingExportPhase> Phases { get; } = new();
        public void Report(double value) { }
        public void ReportPhase(RecordingExportPhase phase) => Phases.Add(phase);
    }

    private sealed class FailProcess(IProjectMediaProcess inner, int failAt) : IProjectMediaProcess
    {
        private int _calls;
        public Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> inputs,
            FileStream? output, CancellationToken ct)
        {
            if (++_calls == failAt) throw new IOException("injected stage failure");
            return inner.RunAsync(job, inputs, output, ct);
        }
    }
}

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task ProjectScopedExport_LegacyRetryCannotSilentlyChangeGifIntoWholeMp4()
    {
        await using var scope = new RecordingScope();
        var mp4 = new ControlledContentExporter(); var gif = new ControlledContentExporter();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder,new JsonProjectStore(),new ProjectProbeStub(),mp4,_=>gif);
        Assert.True((await coordinator.StartNewContentAsync(scope.Configuration,Guid.NewGuid())).Success);
        Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
        var snapshot=coordinator.Current!;
        Assert.True((await coordinator.ExportAsync(scope.Configuration.OutputDirectory,snapshot.Revision,Guid.NewGuid(),
            exportOptions:new(ProjectExportFormat.Gif,ProjectExportScope.Clip,snapshot.Clips[0].Id,new()))).Success);
        await gif.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        gif.Complete.SetResult(new(false,null,"injected"));
        await UntilAsync(()=>coordinator.ExportStatus?.State==RecordingExportState.Failed);
        Assert.False((await coordinator.RetryExportAsync(Guid.NewGuid())).Success);
        Assert.Equal(0,mp4.Calls);
    }

    [Fact]
    public async Task ProjectScopedExport_GifRequestSelectsGifBackendAndRejectsWebCombination()
    {
        await using var scope = new RecordingScope();
        var mp4 = new ControlledContentExporter();
        var gif = new ControlledContentExporter();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(),
            new ProjectProbeStub(), mp4, _ => gif);
        Assert.True((await coordinator.CreateAsync(scope.Configuration.OutputDirectory, "scope")).Success);
        Assert.True((await coordinator.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.True((await coordinator.FinishAsync(Guid.NewGuid())).Success);
        var snapshot = coordinator.Current!;
        var options = new ProjectExportOptions(ProjectExportFormat.Gif, ProjectExportScope.Clip, snapshot.Clips[0].Id, new());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var request = new ProjectRequest { ProjectId = snapshot.ProjectId, ExpectedRevision = snapshot.Revision,
            OperationId = Guid.NewGuid(), Path = scope.Configuration.OutputDirectory, ExportOptions = options, ExportWebPlayer = true };
        var invalid = await dispatcher.DispatchAsync(new() { MessageType = "ExportRecordingContent", PayloadJson = JsonSerializer.Serialize(request) });
        Assert.False(invalid.Success);
        request = request with { OperationId = Guid.NewGuid(), ExportWebPlayer = false };
        var accepted = await dispatcher.DispatchAsync(new() { MessageType = "ExportRecordingContent", PayloadJson = JsonSerializer.Serialize(request) });
        Assert.True(accepted.Success, accepted.ErrorMessage);
        await gif.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, mp4.Calls);
        Assert.Equal(snapshot.Clips[0].Id, Assert.Single(gif.Snapshot!.Clips).Id);
        Assert.Equal(snapshot.Revision, gif.Snapshot.Revision);
        await coordinator.CancelExportAsync(coordinator.ExportStatus!.ExportId);
        await UntilAsync(() => coordinator.ExportStatus?.State == RecordingExportState.Canceled);
    }
}
