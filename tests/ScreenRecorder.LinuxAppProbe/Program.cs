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
using Serilog;

// Actual production service graph, actual X11/Pulse sources, no synthetic capture.
if (!OperatingSystem.IsLinux() || args.Length != 1) return 2;
Serilog.Log.Logger = new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console().CreateLogger();
try {
    var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
    var storage = new StorageService(); var displays = new LinuxDesktopCatalog();
    var provider = new LinuxFFmpegProvider(); var encoders = new FFmpegEncoderDetector(provider);
    var teachingPointer=Environment.GetEnvironmentVariable("OPENCAM_LINUX_TEST_POINTER")=="1";
    using var pointer=new LinuxX11PointerOverlay();
    var display = displays.GetMonitors().FirstOrDefault() ?? throw new IOException("No real Linux capture source.");
    await using var recorder = new RecordingOrchestrator(new RecordingStateMachine(), storage, new JsonRecordingSessionStore(),
        new DiskSpaceMonitor(storage), new StreamCopyRemuxer(), new MediaFileProbe(), displays, provider,
        encoderSelectionService: encoders, engineFactory: new RecordingEngineFactory(provider, encoders, displayService: displays),cursorHighlightService:teachingPointer?pointer:null);
    var ffmpeg = ScreenRecorder.Media.FFmpeg.FFmpegDiscovery.FindFFmpegExecutable() ?? throw new IOException("No bundled FFmpeg.");
    await using var project = new ProjectRecordingCoordinator(recorder, new JsonProjectStore(), new ProjectSourceProbe(),
        new ProjectFfmpegExporter(new LinuxProjectMediaProcess(ffmpeg)), mediaProcess: () => new LinuxProjectMediaProcess(ffmpeg));
    void Check(ProjectCommandResult r) { if (!r.Success) throw new IOException(r.ErrorCode + " / " + recorder.CurrentState); }
    Check(await project.CreateAsync(root, "Linux actual project workflow"));
    var config = new RecordingConfiguration { CaptureSource = CaptureSourceType.Monitor, MonitorIndex = display.Index,
        AudioSource = AudioSourceType.SystemAndMicrophone, MicrophoneDeviceId = "opencam_mic", SystemAudioDeviceId = "opencam_test.monitor",
        CursorEffect = CursorEffectMode.Default, Fps = 30, EncoderType = HardwareEncoderType.SoftwareCpu,
        SystemAudioVolumePercent = 80, MicrophoneVolumePercent = 70, OutputDirectory = root };
    config.Fps = Environment.GetEnvironmentVariable("OPENCAM_LINUX_TEST_FPS") == "20" ? 20 : 30;
    if(teachingPointer){config.CursorEffect=CursorEffectMode.PresentationStick;config.PresentationPointer=new(TeachingPointerStyle.HighContrast,TeachingPointerSize.Standard);}
    var seconds = int.TryParse(Environment.GetEnvironmentVariable("OPENCAM_LINUX_TEST_SECONDS"), out var requested) && requested is >= 5 and <= 300 ? requested : 5;
    await using (var trial = new FileStream(Path.Combine(root, "audio-trial.mkv"), FileMode.CreateNew, FileAccess.Write))
    await using (var preflight = new PreflightAudioCaptureFactory(provider).Create()) {
        await preflight.StartAsync(config, trial, default); await Task.Delay(1200);
        if (!preflight.IsRunning || preflight.ErrorCode is not null) throw new IOException("Linux audio confirmation did not become healthy.");
        await preflight.StopAsync(default);
        if (trial.Length == 0 || preflight.ErrorCode is not null) throw new IOException("Audio trial did not seal cleanly.");
    }
    await using (var playback = new LinuxProjectAudioOutput(Path.Combine(AppContext.BaseDirectory, "OpenCam.PulsePlayback"))) {
        await playback.StartAsync(default); await playback.WriteAsync(new byte[8192], default); await playback.CompleteAsync(default);
        if (playback.PositionSamples != 1024) throw new IOException("Playback clock did not acknowledge actual drained samples.");
    }
    await using (var playback = new LinuxProjectAudioOutput(Path.Combine(AppContext.BaseDirectory, "OpenCam.PulsePlayback"))) {
        await playback.StartAsync(default);
        for (var i = 0; i < 5; i++) await playback.WriteAsync(new byte[8192], default);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (playback.PositionSamples < 1024 && playback.IsRunning && DateTime.UtcNow < deadline) await Task.Delay(20);
        if (playback.PositionSamples < 1024) throw new IOException("Managed bounded audio output did not advance sufficiently before EOF.");
        Console.WriteLine($"Managed bounded audio clock advanced before EOF: {playback.PositionSamples}");
        await playback.StopAsync(default);
    }
    // First real segment at 30 FPS, pause, resume, stop: source files must survive.
    Check(await project.StartAsync(config, Guid.NewGuid()));
    if(teachingPointer&&!pointer.IsRunning)throw new IOException("The live teaching pointer did not become ready.");
    bool systemLive = false, systemSilent = false, micLive = false, micSilent = false;
    for (var tick = 0; tick < seconds * 10; ++tick) {
        if (recorder.CurrentState != RecordingState.Recording) throw new IOException("Recording stopped unexpectedly: " + recorder.CurrentState);
        var levels = recorder.GetAudioLevels(DateTimeOffset.UtcNow);
        systemLive |= levels.SystemAudio.State == AudioMeterState.Live;
        systemSilent |= levels.SystemAudio.State == AudioMeterState.Silent;
        micLive |= levels.Microphone.State == AudioMeterState.Live;
        micSilent |= levels.Microphone.State == AudioMeterState.Silent;
        await Task.Delay(100);
    }
    if (seconds >= 10 && !(systemLive && systemSilent && micLive && micSilent))
        throw new IOException($"Real audio transitions not observed: system live={systemLive}, silent={systemSilent}; microphone live={micLive}, silent={micSilent}");
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
    // Exercise the same streaming decoder/coordinator as the editor Play button.
    // A still frame plus a tiny PCM write followed by EOF is not playback acceptance.
    var decoder = new ProjectPreviewDecoder(() => new LinuxProjectMediaProcess(ffmpeg),
        () => new LinuxProjectAudioOutput(Path.Combine(AppContext.BaseDirectory, "OpenCam.PulsePlayback")));
    await using (var preview = new ProjectPreviewCoordinator(project, decoder.PlayConfiguredAsync)) {
        foreach (var muted in new[] { false, true }) {
            var playback = await preview.PlayAsync(project.Current.ProjectId, project.Current.Revision, 0,
                ProjectPreviewQuality.P360, muted);
            var deadline = DateTime.UtcNow.AddSeconds(12);
            bool advanced = false;
            var reportAt = DateTime.UtcNow;
            while (DateTime.UtcNow < deadline) {
                var snapshot = preview.Query(playback.Generation); // renew actual UI lease
                if (DateTime.UtcNow >= reportAt) {
                    Console.WriteLine($"Preview poll: muted={muted}, playing={snapshot.State.Playing}, ticks={snapshot.State.TimelineTicks}, frame={snapshot.Frame?.TimelineTicks}, error={snapshot.State.Error}");
                    reportAt = DateTime.UtcNow.AddSeconds(1);
                }
                if (snapshot.State.Error is not null) throw new IOException("Streaming editor preview failed: " + snapshot.State.Error);
                if (snapshot.State.TimelineTicks >= TimeSpan.TicksPerSecond && snapshot.Frame is { HasValidPixels: true }) {
                    advanced = true; break;
                }
                if (!snapshot.State.Playing) break;
                await Task.Delay(50);
            }
            await preview.StopAsync();
            if (!advanced) throw new IOException($"Streaming editor preview did not advance (muted={muted}).");
        }
    }
    var output = Path.Combine(root, "exports"); Directory.CreateDirectory(output);
    Check(await project.ExportAsync(output, project.Current.Revision, Guid.NewGuid()));
    for (var attempt = 0; attempt < 600 && project.ExportStatus?.State == RecordingExportState.Running; attempt++) await Task.Delay(100);
    if (project.ExportStatus?.State != RecordingExportState.Succeeded) throw new IOException("Export failed: " + project.ExportStatus?.Error);
    var result = new { status = "PASS", scope = "virtual-source production-services integration, not physical sync",
        fps = config.Fps, firstSegmentSeconds = seconds, teachingPointer, audioPreflightAndPlayback = "PASS", streamingEditorPreview = "PASS", systemLive, systemSilent, micLive, micSilent,
        project = project.ProjectDirectory, sources = project.Current.Sources.Length,
        sourceFiles = project.Current.Sources.Select(s => Path.Combine(project.ProjectDirectory!, s.RelativePath)).ToArray(),
        mp4 = project.ExportStatus.FinalPath };
    await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(result)); return 0;
} catch (Exception e) { Console.Error.WriteLine(e); return 1; }
