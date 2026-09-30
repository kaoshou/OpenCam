// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Media.Encoders;
using ScreenRecorder.Platform.Windows.Capture;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class DdagrabCapabilityProbeTests
{
    internal const string Help = "Filter ddagrab\n output_idx draw_mouse framerate video_size offset_x offset_y output_fmt dup_frames";
    [Theory]
    [InlineData(EncoderProbeStatus.Available, 0, Help, true)]
    [InlineData(EncoderProbeStatus.Available, 0, "Unknown filter 'ddagrab'.", false)]
    [InlineData(EncoderProbeStatus.Available, 0, "Filter ddagrab output_idx", false)]
    [InlineData(EncoderProbeStatus.ProbeFailed, 1, Help, false)]
    [InlineData(EncoderProbeStatus.TimedOut, null, Help, false)]
    public async Task RequiresSuccessfulCompleteHelp(EncoderProbeStatus status, int? exit, string help, bool available)
    {
        var runner = new Runner(new(status, exit, TimeSpan.Zero, help, "", true));
        Assert.Equal(available, await new DdagrabCapabilityProbe(runner).IsAvailableAsync("ffmpeg", default));
        Assert.Equal(new[] { "-hide_banner", "-h", "filter=ddagrab" }, runner.Start!.ArgumentList);
        Assert.Equal(TimeSpan.FromSeconds(3), runner.Timeout);
    }

    [Fact]
    public async Task CancellationAndIncompleteCleanupCannotBecomeFallback()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DdagrabCapabilityProbe(
            new Runner(new(EncoderProbeStatus.Cancelled, null, TimeSpan.Zero, "", "", true))).IsAvailableAsync("ffmpeg", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DdagrabCapabilityProbe(
            new Runner(new(EncoderProbeStatus.TimedOut, null, TimeSpan.Zero, "", "", false))).IsAvailableAsync("ffmpeg", default));
    }
    private sealed class Runner(EncoderProbeResult result) : IEncoderProbeRunner
    {
        public ProcessStartInfo? Start; public TimeSpan Timeout;
        public Task<EncoderProbeResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken token = default)
        { Start = startInfo; Timeout = timeout; return Task.FromResult(result); }
    }
}
