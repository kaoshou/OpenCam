// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;

namespace ScreenRecorder.Recorder.Services;

internal sealed record ProjectMediaBackend(Func<IProjectMediaProcess> CreateProcess,
    Func<IProjectStreamingMediaProcess> CreateStreamingProcess, Func<IProjectAudioOutput>? CreateAudio)
{
    internal static ProjectMediaBackend? Create(OSPlatform platform, string? executable, string macAudioHelper)
    {
        if (executable is null) return null;
        if (platform == OSPlatform.Windows)
            return new(() => new WindowsProjectMediaProcess(executable),
                () => new WindowsProjectMediaProcess(executable), () => new WindowsProjectAudioOutput());
        if (platform == OSPlatform.OSX)
            return new(() => new MacProjectMediaProcess(executable), () => new MacProjectMediaProcess(executable),
                File.Exists(macAudioHelper) ? () => new MacProjectAudioOutput(macAudioHelper) : null);
        if (platform == OSPlatform.Linux)
            return new(() => new ScreenRecorder.Platform.Linux.LinuxProjectMediaProcess(executable),
                () => new ScreenRecorder.Platform.Linux.LinuxProjectMediaProcess(executable),
                () => new ScreenRecorder.Platform.Linux.LinuxProjectAudioOutput(Path.Combine(AppContext.BaseDirectory, "OpenCam.PulsePlayback")));
        return null;
    }
}
