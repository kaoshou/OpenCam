// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;

namespace ScreenRecorder.Media.Tests;

/// <summary>Real libobs/encoder/mux acceptance; synthetic sources never open a screen or audio device.</summary>
public sealed class ObsNativeAcceptanceTests
{
    [ObsNativeFact]
    public async Task RealLibobsStreamsDecodableChangingH264AndAacIntoOwnedOutput()
    {
        var helper = Environment.GetEnvironmentVariable("OPENCAM_OBS_HELPER")!;
        var runtime = Environment.GetEnvironmentVariable("OPENCAM_OBS_RUNTIME")!;
        var path = Path.Combine(Path.GetTempPath(), "opencam-obs-native-" + Guid.NewGuid().ToString("N") + ".mkv");
        var display = new MonitorInfo(0, "synthetic", new(0, 0, 320, 240), true, 1);
        try
        {
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, true))
            await using (var engine = new ObsScreenRecorderEngine(helper, runtime, (_, _, _) => display)
            { UseSyntheticCaptureSource = true, OutputStream = destination })
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await engine.StartRecordingAsync("never-open-this-path", new() { Fps = 30,
                    AudioSource = AudioSourceType.SystemAndMicrophone }, display.Bounds, deadline.Token);
                while (engine.CurrentFramesRecorded < 45) await Task.Delay(20, deadline.Token);
                Assert.Equal("libobs-v1", engine.ActiveCapture?.RecordingCore);
                await engine.StopRecordingAsync(deadline.Token);
                Assert.True(destination.CanWrite);
                Assert.True(destination.Length > 4096);
                Assert.True(engine.CurrentRecordedTime >= TimeSpan.FromSeconds(1));
            }
            var probe = await new MediaFileProbe().ProbeAsync(path);
            Assert.True(probe.IsValid, probe.RawJsonOutput);
            Assert.Equal("h264", probe.VideoCodec);
            Assert.Equal("aac", probe.AudioCodec);
            Assert.Equal(320, probe.Width);
            Assert.Equal(240, probe.Height);
            Assert.Equal(30, probe.Fps);
            Assert.Equal(1, probe.VideoStreamCount);
            Assert.Equal(1, probe.AudioStreamCount);
            var start = new ProcessStartInfo(FFmpegDiscovery.FindFFmpegExecutable()!)
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-v", "error", "-i", path, "-map", "0:v:0", "-f", "framemd5", "-" })
                start.ArgumentList.Add(argument);
            using var decode = Process.Start(start)!;
            var framesTask = decode.StandardOutput.ReadToEndAsync();
            var errorsTask = decode.StandardError.ReadToEndAsync();
            await decode.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var frames = await framesTask;
            var errors = await errorsTask;
            Assert.Equal(0, decode.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(errors), errors);
            var hashes = frames.Split('\n').Where(line => line.Length > 0 && line[0] != '#')
                .Select(line => line.Split(',')[^1].Trim()).ToArray();
            Assert.True(hashes.Length >= 30);
            Assert.True(hashes.Distinct().Count() >= 10, "The recorded video must contain real changing frames.");
        }
        finally { File.Delete(path); }
    }
}

public sealed class ObsNativeFactAttribute : FactAttribute
{
    public ObsNativeFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENCAM_OBS_HELPER")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENCAM_OBS_RUNTIME")))
            Skip = "Requires a built pinned libobs helper/runtime: set OPENCAM_OBS_HELPER and OPENCAM_OBS_RUNTIME. Synthetic acceptance does not verify physical capture.";
    }
}
