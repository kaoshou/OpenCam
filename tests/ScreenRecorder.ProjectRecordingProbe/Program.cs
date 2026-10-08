// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.State;
using ScreenRecorder.Infrastructure.Diagnostics;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Infrastructure.Storage;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Remux;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Recorder.Services;

try
{
    if (args.Length != 2 || args[0] is not ("create" or "append")) return 2;
    var storage = new StorageService();
    var displays = new FixtureDisplay();
    await using var recorder = new RecordingOrchestrator(new RecordingStateMachine(), storage,
        new JsonRecordingSessionStore(), new DiskSpaceMonitor(storage), new StreamCopyRemuxer(), new MediaFileProbe(),
        displays, OperatingSystem.IsWindows() ? new WindowsFFmpegProvider() : new MacOsFFmpegProvider(displays))
        { UseSyntheticCaptureSource = true };
    await using var project = new ProjectRecordingCoordinator(recorder, new JsonProjectStore(), new ProjectSourceProbe());
    var opened = args[0] == "create" ? await project.CreateAsync(args[1], "Cross-process integration") : await project.OpenAsync(args[1]);
    if (!opened.Success) throw new InvalidOperationException(opened.ErrorCode);
    var config = new RecordingConfiguration { AudioSource = AudioSourceType.None, Fps = 30,
        EncoderType = HardwareEncoderType.SoftwareCpu, DeleteWorkingFileAfterSuccessfulRemux = true };
    for (var index = 0; index < (args[0] == "create" ? 3 : 1); index++)
    {
        var start = await project.StartAsync(config, Guid.NewGuid());
        if (!start.Success) throw new InvalidOperationException(start.ErrorCode);
        await Task.Delay(700);
        // Saving the project while recording must neither stop nor commit the live segment.
        var previousSources = project.Current!.Sources.Length;
        var saved = await project.SaveAsync(project.Current.Revision);
        if (!saved.Success || recorder.CurrentState != RecordingState.Recording || project.Current.Sources.Length != previousSources)
            throw new InvalidOperationException("Saving interrupted live recording.");
        var pause = await project.PauseAsync(Guid.NewGuid());
        if (!pause.Success) throw new InvalidOperationException(pause.ErrorCode);
    }
    var finish = await project.FinishAsync(Guid.NewGuid());
    if (!finish.Success) throw new InvalidOperationException(finish.ErrorCode);
    Console.WriteLine($"SOURCES:{project.Current!.Sources.Length}");
    if (!(await project.CloseAsync()).Success) throw new InvalidOperationException("Close failed.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }

sealed class FixtureDisplay : IDisplayService
{
    public IReadOnlyList<MonitorInfo> GetMonitors() => [new(0, "Synthetic fixture", new(0, 0, 320, 240), true, 1)];
    public MonitorInfo? GetPrimaryMonitor() => GetMonitors()[0];
    public CaptureRegion GetVirtualScreenBounds() => new(0, 0, 320, 240);
}
