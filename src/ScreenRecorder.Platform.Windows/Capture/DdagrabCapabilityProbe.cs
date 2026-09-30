// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Media.Encoders;

namespace ScreenRecorder.Platform.Windows.Capture;

public sealed class DdagrabCapabilityProbe(IEncoderProbeRunner? runner = null)
{
    private readonly IEncoderProbeRunner _runner = runner ?? new EncoderProbeRunner();
    public async Task<bool> IsAvailableAsync(string ffmpegPath, CancellationToken token)
    {
        var start = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-hide_banner", "-h", "filter=ddagrab" }) start.ArgumentList.Add(argument);
        var result = await _runner.RunAsync(start, TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
        if (!result.CleanupCompleted) throw new InvalidOperationException("Capture capability probe could not be cleaned up.");
        if (result.Status == EncoderProbeStatus.Cancelled) throw new OperationCanceledException(token);
        token.ThrowIfCancellationRequested();
        var help = result.StdoutTail + "\n" + result.StderrTail;
        return result.Status == EncoderProbeStatus.Available && result.ExitCode == 0
            && new[] { "Filter ddagrab", "output_idx", "draw_mouse", "framerate", "video_size",
                "offset_x", "offset_y", "output_fmt", "dup_frames" }.All(help.Contains);
    }
}
