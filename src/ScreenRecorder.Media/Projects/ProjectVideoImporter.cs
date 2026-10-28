// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.Probe;

namespace ScreenRecorder.Media.Projects;

/// <summary>Prepare and validate project-owned media before its manifest transaction.</summary>
public sealed class ProjectVideoImporter(IProjectMediaProcess process, IProjectSourceProbe probe)
{
    public async Task<ProjectSource> ImportAsync(IProjectHandle project, string path,
        Action<string>? phase, CancellationToken ct)
    {
        phase?.Invoke("Checking");
        using var original = ProjectImportedMedia.OpenOriginal(path);
        if (original.Length == 0) throw new InvalidDataException("VideoImportInvalid");
        var originalSize = original.Length;
        var originalHash = await SHA256.HashDataAsync(original, ct);
        original.Position = 0;
        var inspected = await process.RunAsync(ProjectMediaJob.InspectImport(), [original], null, ct);
        using var json = JsonDocument.Parse(inspected.Output);
        var formatNames = Text(json.RootElement.GetProperty("format"), "format_name")?.Split(',') ?? [];
        if (!formatNames.Any(f => f is "mov" or "mp4" or "matroska" or "webm" or "avi"))
            throw new InvalidDataException("VideoImportInvalid");
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var videos = streams.Where(s => Text(s, "codec_type") == "video").ToArray();
        var audios = streams.Where(s => Text(s, "codec_type") == "audio").ToArray();
        if (videos.Length != 1 || videos[0].TryGetProperty("disposition", out var disposition) &&
            disposition.TryGetProperty("attached_pic", out var picture) && picture.GetInt32() != 0)
            throw new InvalidDataException("VideoImportVideoTrack");
        if (audios.Length > 1) throw new InvalidDataException("VideoImportMultipleAudio");
        var video = videos[0];
        if (Text(video, "color_transfer") is "smpte2084" or "arib-std-b67")
            throw new InvalidDataException("VideoImportHdr");
        var width = video.GetProperty("width").GetInt32();
        var height = video.GetProperty("height").GetInt32();
        if (width is < 2 or > 4096 || height is < 2 or > 4096)
            throw new InvalidDataException("VideoImportDimensions");
        var rotation = video.TryGetProperty("side_data_list", out var side) &&
            side.EnumerateArray().Any(s => s.TryGetProperty("rotation", out var r) && r.GetDouble() != 0);
        rotation |= video.TryGetProperty("tags", out var tags) && Text(tags, "rotate") is { } rotate && rotate != "0";
        var convertVideo = Text(video, "codec_name") != "h264" || Text(video, "pix_fmt") != "yuv420p" ||
            Text(video, "sample_aspect_ratio") is not (null or "1:1" or "0:1") || rotation || width % 2 != 0 || height % 2 != 0;
        var convertAudio = audios.Length == 1 && Text(audios[0], "codec_name") != "aac";
        ct.ThrowIfCancellationRequested();
        var free = new DriveInfo(project.ProjectDirectory).AvailableFreeSpace;
        var limit = free - 256L * 1024 * 1024;
        if (limit < originalSize + 16L * 1024 * 1024) throw new IOException("VideoImportDiskSpace");
        using var staged = new ProjectImportedMedia(project);
        phase?.Invoke(convertVideo || convertAudio ? "Converting" : "Copying");
        await process.RunAsync(ProjectMediaJob.PrepareImport(limit, convertVideo, convertAudio),
            [original], staged.Output, ct);
        await staged.Output.FlushAsync(ct);
        staged.Output.Flush(true);
        // Windows validates exclusively read-only inputs; close the writable staging handle first.
        await staged.Output.DisposeAsync();
        phase?.Invoke("Verifying");
        ProjectSource source;
        using (var owned = staged.OpenStaged())
        {
            var info = await probe.ProbeAsync(owned, ct);
            var seconds = (decimal)info.Timing.DurationTs * info.Timing.TimeBase.Numerator / info.Timing.TimeBase.Denominator;
            if (seconds <= 0 || info.VideoCodec != "h264" ||
                audios.Length == 1 && info.AudioCodec != "aac") throw new InvalidDataException("VideoImportInvalid");
            if (process is not IProjectStreamingMediaProcess streaming) throw new InvalidOperationException("VideoImportBackend");
            // Decode the complete staged file, not only its first frame. Progress is drained
            // incrementally, so long videos do not accumulate decoded frames or logs in memory.
            var progressLimit = checked(Math.Max(1024 * 1024, (long)seconds * 2048));
            await streaming.RunStreamingAsync(ProjectMediaJob.VerifyImport(progressLimit), owned,
                (data, token) => data.CopyToAsync(Stream.Null, token), ct);
            await process.RunAsync(ProjectMediaJob.ExtractFrame(info.Timing.StartPts, info.Timing.TimeBase, 96, 64), [owned], null, ct);
            if (info.AudioCodec is not null)
            {
                var end = info.Timing.StartPts + Math.Min(info.Timing.DurationTs,
                    Math.Max(1, info.Timing.TimeBase.Denominator / info.Timing.TimeBase.Numerator));
                await process.RunAsync(ProjectMediaJob.ExtractPcm(info.Timing.StartPts, end, info.Timing.TimeBase, true), [owned], null, ct);
            }
            owned.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(owned, ct));
            source = new() { Id = staged.Id, SessionId = "imported", RelativePath = staged.RelativePath,
                FileSize = owned.Length, Sha256 = hash, Timing = info.Timing, Width = info.Width, Height = info.Height,
                VideoCodec = info.VideoCodec, AudioCodec = info.AudioCodec };
        }
        original.Position = 0;
        if (original.Length != originalSize || !originalHash.SequenceEqual(await SHA256.HashDataAsync(original, ct)))
            throw new IOException("VideoImportSourceChanged");
        ct.ThrowIfCancellationRequested();
        staged.Publish();
        return source;
    }

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetString() : null;
}
