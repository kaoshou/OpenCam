// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectAudioOutputTests
{
    [NativeProjectAudioFact]
    public async Task DeviceClockDoesNotInventSamplesDuringDecoderGap()
    {
        await using var audio = new MacProjectAudioOutput(Environment.GetEnvironmentVariable("OPENCAM_TEST_PROJECT_AUDIO_HELPER")!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await audio.StartAsync(timeout.Token);
        var block = new byte[8192];
        for (var i = 0; i < 8; i++) await audio.WriteAsync(block, timeout.Token);
        await Task.Delay(500, timeout.Token);
        Assert.InRange(audio.PositionSamples, 1, 8192);
        var before = audio.PositionSamples;
        for (var i = 0; i < 8; i++) await audio.WriteAsync(block, timeout.Token);
        await Task.Delay(500, timeout.Token);
        Assert.InRange(audio.PositionSamples, before + 1, 16384);
        await audio.CompleteAsync(timeout.Token);
        Assert.Equal(16384, audio.PositionSamples);
        await audio.StopAsync(timeout.Token);
        Assert.False(audio.IsRunning);
    }
    [NativeProjectAudioFact]
    public async Task DeviceClockAdvancesAndStopJoinsOutputProcess()
    {
        var helper = Environment.GetEnvironmentVariable("OPENCAM_TEST_PROJECT_AUDIO_HELPER")!;
        await using var audio = new MacProjectAudioOutput(helper);
        for (var run = 0; run < 2; run++)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await audio.StartAsync(timeout.Token);
            var pcm = new byte[1024 * 8];
            for (var i = 0; i < pcm.Length; i += 4) BitConverter.GetBytes(.001f).CopyTo(pcm, i);
            for (var i = 0; i < 12; i++) await audio.WriteAsync(pcm, timeout.Token);
            while (audio.PositionSamples <= 0) await Task.Delay(5, timeout.Token);
            await audio.StopAsync(timeout.Token);
            Assert.False(audio.IsRunning);
            var position = audio.PositionSamples;
            await Task.Delay(250, timeout.Token);
            Assert.Equal(position, audio.PositionSamples);
        }
    }
}

public sealed class NativeProjectAudioFactAttribute : FactAttribute
{
    public NativeProjectAudioFactAttribute()
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENCAM_TEST_PROJECT_AUDIO_HELPER")))
            Skip = "Requires explicitly authorized macOS playback and OPENCAM_TEST_PROJECT_AUDIO_HELPER.";
    }
}
