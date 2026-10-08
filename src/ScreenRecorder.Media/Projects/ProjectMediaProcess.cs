// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

/// <summary>The caller retains ownership and exclusively leases streams until completion.</summary>
public interface IProjectMediaProcess
{
    Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> boundInputs,
        FileStream? boundOutput, CancellationToken ct);
}

/// <summary>One tightly packed RGBA frame, or empty Output when written to the caller's bound stream.</summary>
public sealed record ProjectMediaResult(byte[] Output, string Diagnostic);

/// <summary>Closed set of validated operations, never UI-supplied command-line arguments.</summary>
public sealed class ProjectMediaJob
{
    public const int MaximumFrameBytes = 64 * 1024 * 1024;
    public const int MaximumDiagnosticBytes = 1024 * 1024;
    public long SourcePts { get; }
    public ProjectRational TimeBase { get; }
    public int Width { get; }
    public int Height { get; }
    public int ExpectedOutputBytes => checked(Width * Height * 4);

    private ProjectMediaJob(long pts, ProjectRational timeBase, int width, int height)
        => (SourcePts, TimeBase, Width, Height) = (pts, timeBase, width, height);

    public static ProjectMediaJob ExtractFrame(long pts, ProjectRational timeBase, int width, int height)
    {
        // FFmpeg filter expressions use doubles: reject PTS that could select an adjacent integer.
        if (pts is < -9_007_199_254_740_991 or > 9_007_199_254_740_991)
            throw new ArgumentOutOfRangeException(nameof(pts));
        if (timeBase is null || timeBase.Numerator is <= 0 or > int.MaxValue ||
            timeBase.Denominator is <= 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeBase));
        if (width is <= 0 or > 4096 || height is <= 0 or > 4096 ||
            (long)width * height * 4 > MaximumFrameBytes)
            throw new ArgumentOutOfRangeException(nameof(width));
        // FFmpeg's time parser uses signed microseconds. Reject unrepresentable requests.
        var seconds = (decimal)pts * timeBase.Numerator / timeBase.Denominator;
        if (seconds < long.MinValue / 1_000_000m || seconds > long.MaxValue / 1_000_000m)
            throw new ArgumentOutOfRangeException(nameof(pts));
        return new(pts, timeBase, width, height);
    }

    internal string[] FileDescriptorArguments() => [
        "-nostdin", "-hide_banner", "-loglevel", "error", "-copyts",
        "-protocol_whitelist", "fd,pipe",
        ..(SourcePts >= 0 ? new[] { "-noaccurate_seek", "-seek_timestamp", "1", "-ss",
            ((decimal)SourcePts * TimeBase.Numerator / TimeBase.Denominator).ToString(CultureInfo.InvariantCulture) }
            : Array.Empty<string>()),
        "-i", "fd:", "-map", "0:v:0", "-an", "-sn", "-dn",
        "-vf", FormattableString.Invariant($"select=gte(pts\\,{SourcePts}),scale={Width}:{Height}:force_original_aspect_ratio=decrease,pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2:color=black"),
        "-frames:v", "1", "-fps_mode", "passthrough", "-threads", "1",
        "-c:v", "rawvideo", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"
    ];
}
