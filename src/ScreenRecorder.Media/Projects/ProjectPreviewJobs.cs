// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace ScreenRecorder.Media.Projects;

public sealed partial class ProjectMediaJob
{
    /// <summary>Same composition as export, streamed as fixed-sized RGBA output frames.</summary>
    public static ProjectMediaJob PreviewVideo(ProjectRenderPlan plan, int index, long skipFrames, int width = 512, int height = 288)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 > MaximumFrameBytes)
            throw new ArgumentOutOfRangeException(nameof(width));
        var export = EncodeClipVideo(plan, index);
        var count = plan.Clips[index].EndFrame - plan.Clips[index].StartFrame - skipFrames;
        if (skipFrames < 0 || count <= 0) throw new ArgumentOutOfRangeException(nameof(skipFrames));
        var args = export._exportArguments!;
        var filterIndex = Array.IndexOf(args, "-filter_complex");
        var filter = args[filterIndex + 1] + FormattableString.Invariant(
            $";[out]trim=start_frame={skipFrames},scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black,format=rgba[preview]");
        var length = checked(count * width * height * 4);
        return new(export.SourcePts, export.TimeBase, width, height) { IsLongRunning = true,
            OutputLimit = length, ExactLength = length,
            _exportArguments = [..args[..filterIndex], "-filter_complex", filter, "-map", "[preview]", "-an", "-sn", "-dn",
                "-fps_mode", "passthrough", "-frames:v", count.ToString(CultureInfo.InvariantCulture), "-c:v", "rawvideo",
                "-pix_fmt", "rgba", "-threads", "1", "-f", "rawvideo", "pipe:1"] };
    }

    /// <summary>Seeking slices already-placed PCM, preserving original fade position and silence.</summary>
    public static ProjectMediaJob PreviewAudio(ProjectRenderPlan plan, int index, long skipSamples)
    {
        var export = RenderClipAudio(plan, index);
        var count = plan.Clips[index].EndAudioSample - plan.Clips[index].StartAudioSample - skipSamples;
        if (skipSamples < 0 || count <= 0) throw new ArgumentOutOfRangeException(nameof(skipSamples));
        var args = export._exportArguments!.ToArray();
        var filterIndex = Array.IndexOf(args, "-filter_complex") + 1;
        args[filterIndex] += FormattableString.Invariant($";[a]atrim=start_sample={skipSamples},asetpts=PTS-STARTPTS[preview-a]");
        args[Array.IndexOf(args, "-map") + 1] = "[preview-a]";
        return new(export.SourcePts, export.TimeBase, 0, 0) { IsLongRunning = true,
            OutputLimit = checked(count * 8), ExactLength = checked(count * 8), _exportArguments = args };
    }
}
