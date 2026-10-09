// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectMediaBackendTests
{
    [Fact]
    public async Task WindowsRegistersBothExportAndAudibleStreamingWithoutMacHelper()
    {
        var backend = ProjectMediaBackend.Create(OSPlatform.Windows, Path.GetFullPath("ffmpeg.exe"), "/missing-mac-helper");
        Assert.NotNull(backend);
        Assert.IsType<WindowsProjectMediaProcess>(backend.CreateProcess());
        Assert.IsType<WindowsProjectMediaProcess>(backend.CreateStreamingProcess());
        await using var audio = backend.CreateAudio!();
        Assert.IsType<WindowsProjectAudioOutput>(audio);
    }
    [Fact]
    public void MissingExecutableDoesNotAdvertiseMediaBackend() =>
        Assert.Null(ProjectMediaBackend.Create(OSPlatform.Windows, null, "/missing-mac-helper"));
}
