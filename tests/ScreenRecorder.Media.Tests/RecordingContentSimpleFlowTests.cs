// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Core.State;
using ScreenRecorder.Infrastructure.Diagnostics;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Infrastructure.Storage;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Remux;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class RecordingContentSimpleFlowTests
{
    [MacOsOnlyFact]
    public async Task NoEditor_StartStopProducesVerifiedMp4AndKeepsReopenableOriginals()
    {
        var directory = Directory.CreateTempSubdirectory("OpenCam-simple-flow-");
        try
        {
            var storage = new StorageService();
            var display = new SyntheticDisplay();
            await using var recorder = new RecordingOrchestrator(new RecordingStateMachine(), storage,
                new JsonRecordingSessionStore(), new DiskSpaceMonitor(storage), new StreamCopyRemuxer(), new MediaFileProbe(),
                display, new MacOsFFmpegProvider(display)) { UseSyntheticCaptureSource = true };
            var media = new TracingMediaProcess(new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!));
            await using var coordinator = new ProjectRecordingCoordinator(recorder, new JsonProjectStore(),
                new ProjectSourceProbe(), new ProjectFfmpegExporter(media));
            var dispatcher = new ProjectIpcDispatcher(coordinator);
            // This fixture never constructs a player or opens an editor/audio device.
            var controller = new RecordingContentController(new ProjectClient((command, request, ct) =>
                dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) })), _ => Task.CompletedTask);
            var config = new RecordingConfiguration { OutputDirectory = directory.FullName, Fps = 60, ProjectName = "Lesson / 課程",
                AudioSource = AudioSourceType.None, EncoderType = HardwareEncoderType.SoftwareCpu,
                DeleteWorkingFileAfterSuccessfulRemux = true };
            await controller.StartNewAsync(config);
            Assert.Equal(ProjectMode.Recording, controller.Workspace.State.Mode);
            await Task.Delay(700);
            await controller.StopAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (coordinator.ExportStatus?.State == RecordingExportState.Running)
                await Task.Delay(25, deadline.Token);
            await controller.RefreshAsync();
            Assert.True(coordinator.ExportStatus?.State == RecordingExportState.Succeeded,
                coordinator.ExportStatus?.Error ?? controller.Workspace.Error ?? "Missing export status");
            var output = coordinator.ExportStatus!.FinalPath!;
            Assert.Equal(0, media.VideoEncodeJobs);
            Assert.True(File.Exists(output));
            Assert.StartsWith("OpenCam_Lesson - 課程_", Path.GetFileName(output));
            Assert.Equal(new ProjectCanvas(320, 240, new(60, 1)), coordinator.Current!.Canvas);
            var info = await new MediaFileProbe().ProbeAsync(output);
            Assert.True(info.IsValid);
            Assert.Equal(320, info.Width);
            Assert.Equal(240, info.Height);
            Assert.Equal(60, info.Fps);
            Assert.True(info.Duration > TimeSpan.FromMilliseconds(400));
            var manifest = Path.Combine(coordinator.ProjectDirectory!, "project.opencam");
            var original = Assert.Single(coordinator.Current.Sources);
            var sourcePath = Path.Combine(coordinator.ProjectDirectory!, original.RelativePath);
            Assert.Equal(original.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))));
            Assert.True(await controller.Workspace.CloseAsync());
            await controller.Workspace.OpenAsync(manifest);
            Assert.Single(controller.Workspace.Clips);
            Assert.True(controller.CanOpenEditor);
            // Saving without requesting export leaves the existing MP4 alone.
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(output));
            await controller.Workspace.SaveAsync();
            Assert.Single(Directory.GetFiles(directory.FullName, "*.mp4"));
            Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(output)));
        }
        finally { directory.Delete(true); }
    }

    private sealed class SyntheticDisplay : IDisplayService
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => [new(0, "Synthetic fixture", new(0, 0, 320, 240), true, 1)];
        public MonitorInfo? GetPrimaryMonitor() => GetMonitors()[0];
        public CaptureRegion GetVirtualScreenBounds() => new(0, 0, 320, 240);
    }

    private sealed class TracingMediaProcess(IProjectMediaProcess inner) : IProjectMediaProcess, IProjectStreamingMediaProcess
    {
        public int VideoEncodeJobs { get; private set; }
        public Task RunStreamingAsync(ProjectMediaJob job, FileStream source, Func<Stream, CancellationToken, Task> consume, CancellationToken ct)
            => ((IProjectStreamingMediaProcess)inner).RunStreamingAsync(job, source, consume, ct);
        public async Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> boundInputs,
            FileStream? boundOutput, CancellationToken ct)
        {
            if (job.FileDescriptorArguments().Contains("libx264")) VideoEncodeJobs++;
            try { return await inner.RunAsync(job, boundInputs, boundOutput, ct); }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"{ex.Message}; output bytes={boundOutput?.Length}; args={string.Join(' ', job.FileDescriptorArguments())}", ex);
            }
        }
    }
}
