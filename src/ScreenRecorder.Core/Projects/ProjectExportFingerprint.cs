// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;

namespace ScreenRecorder.Core.Projects;

public static class ProjectExportFingerprint
{
    public const string Profile = "h264-aac-v1";
    public static string Compute(RecordingProject project, ProjectExportOptions options)
    {
        var selected = ProjectExportSelection.Create(project, options);
        if (options.Format == ProjectExportFormat.Mp4 && options.Scope == ProjectExportScope.WholeProject &&
            options.Mp4 is not { IsOriginal: false })
            return Compute(selected, Profile);
        if (options.Mp4 is { IsOriginal: false } profile)
        {
            var compressed = new { Profile = "h264-aac-options-v1", options.Format, options.Scope,
                Content = Compute(selected, Profile), Mp4 = profile };
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(compressed)));
        }
        var payload = new {
            Profile = options.Format == ProjectExportFormat.Gif ? "gif-palette-v1" : Profile,
            options.Format, options.Scope,
            Content = Compute(selected, Profile),
            Gif = options.Format == ProjectExportFormat.Gif ? options.Gif ?? new GifExportOptions() : null,
            InfiniteLoop = options.Format == ProjectExportFormat.Gif
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
    }
    public static string Compute(RecordingProject project, string exportProfile)
    {
        ProjectValidation.Validate(project);
        project = ProjectClipProjection.Expand(project);
        var payload = new { Profile = exportProfile, project.Canvas, Clips = project.Clips.Select(c => new {
            Source = project.Sources.Single(s => s.Id == c.SourceId), c.InPts, c.OutPts, c.Volume, c.Muted,
            c.FadeInTicks, c.FadeOutTicks, c.Crop, c.Scale, c.PositionX, c.PositionY }).ToArray() };
        if (project.Sources.Any(s => s.Pointer != null))
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
                Content = payload, project.PointerSettings, PointerProfile = "pointer-v1" })));
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
    }
}

public sealed record ProjectExportReceipt(string ContentFingerprint, string Path, long Length, string Sha256);
