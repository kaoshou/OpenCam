// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;

namespace ScreenRecorder.Core.Projects;

public static class ProjectExportFingerprint
{
    public const string Profile = "h264-aac-v1";
    public static string Compute(RecordingProject project, string exportProfile)
    {
        ProjectValidation.Validate(project);
        var payload = new { Profile = exportProfile, project.Canvas, Clips = project.Clips.Select(c => new {
            Source = project.Sources.Single(s => s.Id == c.SourceId), c.InPts, c.OutPts, c.Volume, c.Muted,
            c.FadeInTicks, c.FadeOutTicks, c.Crop, c.Scale, c.PositionX, c.PositionY }).ToArray() };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
    }
}

public sealed record ProjectExportReceipt(string ContentFingerprint, string Path, long Length, string Sha256);
