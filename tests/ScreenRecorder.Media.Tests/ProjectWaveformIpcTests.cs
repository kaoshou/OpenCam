// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Platform.macOS;
using System.Security.Cryptography;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [MacOsOnlyFact]
    public async Task ProjectWaveform_RealSavedMediaReachesEditorThroughIpc()
    {
        var evidence = Environment.GetEnvironmentVariable("OPENCAM_TEST_EVIDENCE_DIR");
        var root = string.IsNullOrWhiteSpace(evidence)
            ? Directory.CreateTempSubdirectory("OpenCam-real-waveform-")
            : Directory.CreateDirectory(Path.Combine(evidence, "actual-project-" + Guid.NewGuid().ToString("N")));
        try
        {
            var store = new JsonProjectStore();
            string projectPath;
            await using (var owner = await store.CreateAsync(root.FullName, "波形驗收 — 測試素材"))
            {
                projectPath = Path.Combine(owner.ProjectDirectory, "project.opencam");
                var path = Path.Combine(owner.ProjectDirectory, "sources", "fixture.mkv");
                await ProjectPcmExtractionTests.Generate(path, true);
                await using var stream = File.OpenRead(path);
                var media = await new ProjectSourceProbe().ProbeAsync(stream);
                stream.Position = 0;
                var source = new ProjectSource { Id = Guid.NewGuid(), SessionId = "fixture",
                    RelativePath = "sources/fixture.mkv", FileSize = stream.Length,
                    Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream)), Timing = media.Timing,
                    Width = media.Width, Height = media.Height, VideoCodec = media.VideoCodec, AudioCodec = media.AudioCodec };
                await owner.SaveAsync(owner.Current with { Revision = 1, Sessions = ["fixture"], Sources = [source],
                    Clips = [new ProjectClip { Id = Guid.NewGuid(), SourceId = source.Id, Name = "含收音的測試片段",
                        InPts = 2000, OutPts = 3000, PositionX = 50 }] }, 0);
            }
            await using var scope = new RecordingScope();
            await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, store, new ProjectSourceProbe());
            await using var waves = new ProjectWaveformService(coordinator,
                new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!));
            await using var frames = new ProjectFrameService(coordinator,
                new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!));
            var dispatcher = new ProjectIpcDispatcher(coordinator, waves, frames);
            var pipe = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());
            var key = AuthenticatedIpc.CreateKey();
            await using var server = new NamedPipeIpcServer(pipe, key, dispatcher.DispatchAsync);
            server.Start();
            await using var mediaServer = new NamedPipeIpcServer(pipe + "-frames", key, dispatcher.DispatchMediaAsync);
            mediaServer.Start();
            await using var transport = new NamedPipeIpcClient(pipe, key);
            var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) =>
                transport.SendCommandAsync(command, request, cancellationToken: ct),
                (request, ct) => transport.SendProjectFrameAsync(request, ct)));
            await vm.OpenAsync(projectPath).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(vm.CanEdit, vm.Error);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (vm.Waveforms.Count == 0) {
                await vm.PollWaveformAsync(); await Task.Delay(20, timeout.Token);
            }
            var data = Assert.Single(vm.Waveforms).Value;
            Assert.Equal(48000, data.SampleCount);
            Assert.Equal(0, data.Buckets[10].Rms);
            Assert.InRange(data.Buckets[70].Rms, .24f, .26f);
            Assert.Equal(0, data.Buckets[200].Rms);
            while (vm.PreviewFrame is null) {
                await vm.PollPreviewAsync(); await Task.Delay(20, timeout.Token);
            }
            var pixels = vm.PreviewFrame.Rgba!;
            Assert.Equal(ProjectFrameReply.ByteCount, pixels.Length);
            // A paused preview must apply the same placement as playback/export.
            var left = (144 * 512 + 100) * 4;
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, pixels[left..(left + 4)]);
            var center = (144 * 512 + 400) * 4;
            Assert.InRange(pixels[center], 0, 4);
            Assert.InRange(pixels[center + 2], 245, 255); // Actual fixture is blue, not a placeholder.
            Assert.Equal(255, pixels[center + 3]);
            Assert.True(await vm.CloseAsync());
            if (!string.IsNullOrWhiteSpace(evidence))
                await File.WriteAllTextAsync(Path.Combine(evidence, "latest-project.txt"), projectPath);
        }
        finally { if (string.IsNullOrWhiteSpace(evidence)) root.Delete(true); }
    }

    [Fact]
    public async Task ProjectWaveform_ResumeCancelsDecoderBeforeCapture()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var decoder = new HeldPcmProcess();
        await using var waveforms = new ProjectWaveformService(coordinator, decoder);
        var dispatcher = new ProjectIpcDispatcher(coordinator, waveforms);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })));
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "Waveform stop");
        await vm.StartAsync(scope.Configuration);
        await vm.PauseAsync();
        await vm.PollWaveformAsync();
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await vm.StartAsync(scope.Configuration).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(decoder.Canceled.Task.IsCompleted);
        Assert.True(vm.CanPause);
        await vm.FinishAsync();
    }

    [Fact]
    public async Task ProjectWaveform_BackgroundDecodeDoesNotBlockSaveAndStaleResultCannotPublish()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var decoder = new HeldPcmProcess();
        await using var waveforms = new ProjectWaveformService(coordinator, decoder);
        var dispatcher = new ProjectIpcDispatcher(coordinator, waveforms);
        var client = new ProjectClient((command, request, ct) => dispatcher.DispatchAsync(new() {
            MessageType = command, PayloadJson = JsonSerializer.Serialize(request) }));
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "Waveform");
        await vm.StartAsync(scope.Configuration);
        await vm.PauseAsync();
        var revision = vm.State.Revision;
        var clip = vm.Clips[0];
        await vm.PollWaveformAsync();
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await vm.RenameAsync(clip.Id, "Renamed").WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(vm.State.Revision > revision);
        Assert.Empty(vm.Waveforms);
        decoder.Release.TrySetResult();
        for (var i = 0; i < 50 && vm.Waveforms.Count == 0; i++) {
            await vm.PollWaveformAsync(); await Task.Delay(10);
        }
        Assert.Single(vm.Waveforms);
        Assert.True(vm.Waveforms[clip.Id].IsSilent);
        Assert.False((await client.SendAsync("GetProjectWaveform", new() {
            ProjectId = vm.State.ProjectId, ExpectedRevision = revision, ClipId = clip.Id })).Success);
        await vm.FinishAsync();
    }

    private sealed class HeldPcmProcess : IProjectMediaProcess
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> input,
            FileStream? output, CancellationToken ct)
        {
            Started.TrySetResult();
            try { await Release.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { Canceled.TrySetResult(); throw; }
            Assert.True(input[0].CanRead);
            return new(new byte[job.ExpectedOutputBytes], "");
        }
    }
}
