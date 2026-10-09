// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public enum ProjectPreviewQuality { P360, P720, P1080, Original }
public sealed record ProjectPreviewSize(int Width, int Height)
{
    public int ByteCount => checked(Width * Height * 4);
}

public static class ProjectPreviewFormat
{
    public const int Width = 1280;
    public const int Height = 720;
    public const int ByteCount = Width * Height * 4;
    public static ProjectPreviewSize Resolve(ProjectCanvas canvas, ProjectPreviewQuality quality)
    {
        if (canvas.Width is <= 0 or > 16384 || canvas.Height is <= 0 or > 16384 || !Enum.IsDefined(quality))
            throw new ArgumentOutOfRangeException(nameof(quality));
        if (quality == ProjectPreviewQuality.Original)
        {
            if ((long)canvas.Width * canvas.Height * 4 > 64 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(canvas));
            return new(canvas.Width, canvas.Height);
        }
        var shortSide = quality switch { ProjectPreviewQuality.P360 => 360, ProjectPreviewQuality.P720 => 720, _ => 1080 };
        var longSide = shortSide * 16 / 9;
        var boundW = canvas.Width > canvas.Height ? longSide : shortSide;
        var boundH = canvas.Width < canvas.Height ? longSide : shortSide;
        var scale = Math.Min(1d, Math.Min((double)boundW / canvas.Width, (double)boundH / canvas.Height));
        return new(Math.Max(1, (int)Math.Floor(canvas.Width * scale)), Math.Max(1, (int)Math.Floor(canvas.Height * scale)));
    }
}
