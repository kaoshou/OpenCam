// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Core.State;
using ScreenRecorder.Infrastructure.Diagnostics;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Infrastructure.Storage;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Remux;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Media.Encoders;
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Recorder.Services;

// Actual production service graph, actual X11/Pulse sources, no synthetic capture.
if (!OperatingSystem.IsLinux() || args.Length != 1) return 2;
try {
    var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
    var storage = new StorageService(); var displays = new LinuxDesktopCatalog();
    var provider = new LinuxFFmpegProvider(); var encoders = new FFmpegEncoderDetector(provider);
    var display = displays.GetMonitors().FirstOrDefault() ?? throw new IOException("No real Linux capture source.");
    await using var recorder = new RecordingOrchestrator(new RecordingStateMachine(), storage, new JsonRecordingSessionStore(),
        new DiskSpaceMonitor(storage), new StreamCopyRemuxer(), new MediaFileProbe(), displays, provider,
        encoderSelectionService: encoders, engineFactory: new RecordingEngineFactory(provider, encoders, displayService: displays));
    var ffmpeg = ScreenRecorder.Media.FFmpeg.FFmpegDiscovery.FindFFmpegExecutable() ?? throw new IOException("No bundled FFmpeg.");
    await using var project = new ProjectRecordingCoordinator(recorder, new JsonProjectStore(), new ProjectSourceProbe(),
        new ProjectFfmpegExporter(new LinuxProjectMediaProcess(ffmpeg)), mediaProcess: () => new LinuxProjectMediaProcess(ffmpeg));
    void Check(ProjectCommandResult r) { if (!r.Success) throw new IOException(r.ErrorCode + " / " + recorder.CurrentState); }
    Check(await project.CreateAsync(root, "Linux actual project workflow"));
    var config = new RecordingConfiguration { CaptureSource = CaptureSourceType.Monitor, MonitorIndex = display.Index,
        AudioSource = AudioSourceType.SystemAndMicrophone, MicrophoneDeviceId = "opencam_mic", SystemAudioDeviceId = "opencam_test.monitor",
        CursorEffect = CursorEffectMode.Default, Fps = 30, EncoderType = HardwareEncoderType.SoftwareCpu,
        SystemAudioVolumePercent = 80, MicrophoneVolumePercent = 70, OutputDirectory = root };
    // First real segment at 30 FPS, pause, resume, stop: source files must survive.
    Check(await project.StartAsync(config, Guid.NewGuid())); await Task.Delay(5000);
    Check(await project.PauseAsync(Guid.NewGuid()));
    Check(await project.StartAsync(config, Guid.NewGuid())); await Task.Delay(3000);
    Check(await project.FinishAsync(Guid.NewGuid()));
    if (project.Current!.Sources.Length != 2) throw new IOException("Pause/resume did not retain two sources.");
    await using var frames = new ProjectFrameService(project, new LinuxProjectMediaProcess(ffmpeg));
    ScreenRecorder.Infrastructure.IPC.ProjectFrameReply? frame = null;
    for (var attempt = 0; attempt < 100; attempt++) {
        frame = await frames.QueryAsync(project.Current.ProjectId, project.Current.Revision, 0);
        if (frame.Rgba is not null) break;
        await Task.Delay(100);
    }
    if (frame?.Rgba is null) throw new IOException("Editor frame was not decoded.");
    var output = Path.Combine(root, "exports"); Directory.CreateDirectory(output);
    Check(await project.ExportAsync(output, project.Current.Revision, Guid.NewGuid()));
    for (var attempt = 0; attempt < 600 && project.ExportStatus?.State == RecordingExportState.Running; attempt++) await Task.Delay(100);
    if (project.ExportStatus?.State != RecordingExportState.Succeeded) throw new IOException("Export failed: " + project.ExportStatus?.Error);
    var result = new { status = "PASS", scope = "virtual-source production-services integration, not physical sync",
        project = project.ProjectDirectory, sources = project.Current.Sources.Length, mp4 = project.ExportStatus.FinalPath };
    await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(result)); return 0;
} catch (Exception e) { Console.Error.WriteLine(e); return 1; }
