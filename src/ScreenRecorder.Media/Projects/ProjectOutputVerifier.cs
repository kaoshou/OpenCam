// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;

namespace ScreenRecorder.Media.Projects;

/// <summary>Checks independently decoded stream counts and format before atomic publication.</summary>
public static class ProjectOutputVerifier
{
    public static async Task VerifyAsync(IProjectMediaProcess process, FileStream output, ProjectRenderPlan plan, CancellationToken ct)
    {
        var result = await process.RunAsync(ProjectMediaJob.InspectEditedMp4(), [output], null, ct);
        using var json = JsonDocument.Parse(result.Output, new JsonDocumentOptions { MaxDepth = 16 });
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Length != 2) throw new InvalidDataException("Output must contain exactly video and audio.");
        var video = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
        var audio = streams.Single(s => s.GetProperty("codec_type").GetString() == "audio");
        if (video.GetProperty("codec_name").GetString() != "h264" ||
            video.GetProperty("width").GetInt32() != plan.Canvas.Width ||
            video.GetProperty("height").GetInt32() != plan.Canvas.Height ||
            !long.TryParse(video.GetProperty("nb_read_frames").GetString(), out var frames) || frames != plan.FrameCount ||
            audio.GetProperty("codec_name").GetString() != "aac" || audio.GetProperty("sample_rate").GetString() != "48000" ||
            audio.GetProperty("channels").GetInt32() != 2 ||
            !long.TryParse(audio.GetProperty("nb_read_frames").GetString(), out var packets) || packets <= 0)
            throw new InvalidDataException("Decoded export streams do not match the render plan.");
        var videoDuration = ReadDuration(video);
        var audioDuration = ReadDuration(audio);
        var expectedVideo = (double)plan.FrameCount * plan.Canvas.Fps.Denominator / plan.Canvas.Fps.Numerator;
        var expectedAudio = (double)plan.AudioSampleCount / 48000;
        if (Math.Abs(videoDuration - expectedVideo) > 0.002 || Math.Abs(audioDuration - expectedAudio) > 1024d / 48000 + 0.002)
            throw new InvalidDataException("Export duration does not match the retained timeline.");
    }

    private static double ReadDuration(JsonElement stream)
    {
        if (!double.TryParse(stream.GetProperty("duration").GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var duration) || !double.IsFinite(duration) || duration <= 0)
            throw new InvalidDataException("Output duration is missing or invalid.");
        return duration;
    }
}
