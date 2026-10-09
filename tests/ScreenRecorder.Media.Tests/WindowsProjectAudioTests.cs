// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Windows;

namespace ScreenRecorder.Media.Tests;

public sealed class WindowsProjectAudioTests
{
    [Fact]
    public async Task InvalidPcmCannotReachTheDevice()
    {
        await using var output = new WindowsProjectAudioOutput();
        await Assert.ThrowsAsync<InvalidDataException>(() => output.WriteAsync(new byte[7], default));
        await Assert.ThrowsAsync<InvalidDataException>(() => output.WriteAsync(new byte[16384], default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => output.WriteAsync(new byte[8], default));
    }

    [WindowsAudioFact]
    public async Task DevicePositionStopsAtSubmittedSamplesDuringDecoderGapAndStopJoins()
    {
        await using var output = new WindowsProjectAudioOutput();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await output.StartAsync(timeout.Token);
        for (var i = 0; i < 6; i++) await output.WriteAsync(new byte[800 * 8], timeout.Token);
        while (output.PositionSamples < 4800) await Task.Delay(10, timeout.Token);
        await Task.Delay(150, timeout.Token);
        Assert.Equal(4800, output.PositionSamples);
        await output.CompleteAsync(timeout.Token);
        await output.StopAsync(timeout.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => output.WriteAsync(new byte[8], timeout.Token));
    }
}

internal sealed class WindowsAudioFactAttribute : FactAttribute
{
    public WindowsAudioFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("OPENCAM_TEST_WINDOWS_AUDIO") != "1")
            Skip = "Requires Windows output device and OPENCAM_TEST_WINDOWS_AUDIO=1.";
    }
}
