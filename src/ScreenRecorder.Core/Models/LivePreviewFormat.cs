// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Models;

/// <summary>Disposable screen-check pixels, never a saved recording format.</summary>
public static class LivePreviewFormat
{
    public const int Width = 960, Height = 540, FramesPerSecond = 10;
    public const int FrameBytes = Width * Height * 4;

    public static string OutputArguments(bool hasAudio, string videoFilter = "")
    {
        // Consume the existing audio maps/filter outputs without putting audio
        // in the raw pixel stream. The null muxer writes no bytes and no file.
        var audio = hasAudio ? "-vn -c:a pcm_s16le -f null - " : "";
        var prefix = videoFilter.Length == 0 ? "" : videoFilter + ",";
        return audio + "-map 0:v:0 -an -vf \"" + prefix +
            $"fps={FramesPerSecond},scale={Width}:{Height}:force_original_aspect_ratio=decrease:flags=fast_bilinear," +
            $"pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2\" " +
            "-c:v rawvideo -pix_fmt rgba -threads:v 1 -fps_mode passthrough -flush_packets 1 -f rawvideo pipe:1";
    }
}
