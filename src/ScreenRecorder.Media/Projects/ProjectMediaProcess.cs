// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Numerics;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

/// <summary>The caller retains ownership and exclusively leases streams until completion.</summary>
public interface IProjectMediaProcess
{
    Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> boundInputs,
        FileStream? boundOutput, CancellationToken ct);
}

/// <summary>RGBA pixels or mono 48 kHz float32 little-endian PCM; empty Output when written to the bound stream.</summary>
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
    public int AudioSampleCount { get; }
    public bool IsAudio => AudioSampleCount > 0;
    public bool HasAudio { get; }
    public long EndPts { get; }
    public long? FrameEndPts { get; private init; }
    public int ExpectedOutputBytes => IsAudio ? checked(AudioSampleCount * 4) : checked(Width * Height * 4);

    private ProjectMediaJob(long pts, ProjectRational timeBase, int width, int height,
        int audioSamples = 0, bool hasAudio = false, long endPts = 0)
        => (SourcePts, TimeBase, Width, Height, AudioSampleCount, HasAudio, EndPts) =
            (pts, timeBase, width, height, audioSamples, hasAudio, endPts);

    public static ProjectMediaJob ExtractFrame(long pts, ProjectRational timeBase, int width, int height, long? endPts = null)
    {
        ValidateTimestamp(pts, timeBase);
        if (width is <= 0 or > 4096 || height is <= 0 or > 4096 ||
            (long)width * height * 4 > MaximumFrameBytes)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (endPts is { } end)
        {
            ValidateTimestamp(end, timeBase);
            if (end <= pts) throw new ArgumentOutOfRangeException(nameof(endPts));
        }
        return new(pts, timeBase, width, height) { FrameEndPts = endPts };
    }

    /// <summary>Bounded audio chunk. Longer media is consumed as successive chunks, never loaded whole.</summary>
    public static ProjectMediaJob ExtractPcm(long startPts, long endPts, ProjectRational timeBase, bool hasAudio)
    {
        ValidateTimestamp(startPts, timeBase);
        ValidateTimestamp(endPts, timeBase);
        var numerator = ((BigInteger)endPts - startPts) * timeBase.Numerator * 48000;
        var samples = (numerator + timeBase.Denominator - 1) / timeBase.Denominator;
        if (endPts <= startPts || samples <= 0 || samples > 30 * 48000)
            throw new ArgumentOutOfRangeException(nameof(endPts), "Audio chunks must be positive and at most 30 seconds.");
        return new(startPts, timeBase, 0, 0, (int)samples, hasAudio, endPts);
    }

    private static void ValidateTimestamp(long pts, ProjectRational timeBase)
    {
        // FFmpeg filter expressions use doubles: reject PTS that could select an adjacent integer.
        if (pts is < -9_007_199_254_740_991 or > 9_007_199_254_740_991)
            throw new ArgumentOutOfRangeException(nameof(pts));
        if (timeBase is null || timeBase.Numerator is <= 0 or > int.MaxValue ||
            timeBase.Denominator is <= 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeBase));
        // FFmpeg's time parser uses signed microseconds. Reject unrepresentable requests.
        var seconds = (decimal)pts * timeBase.Numerator / timeBase.Denominator;
        if (seconds < long.MinValue / 1_000_000m || seconds > long.MaxValue / 1_000_000m)
            throw new ArgumentOutOfRangeException(nameof(pts));
    }

    private string[] InputArguments() => [
        "-nostdin", "-hide_banner", "-loglevel", "error", "-copyts",
        "-protocol_whitelist", "fd,pipe",
        ..(SourcePts >= 0 ? new[] { "-noaccurate_seek", "-seek_timestamp", "1", "-ss",
            ((decimal)SourcePts * TimeBase.Numerator / TimeBase.Denominator).ToString(CultureInfo.InvariantCulture) }
            : Array.Empty<string>()),
        "-i", "fd:"
    ];

    internal string[] FileDescriptorArguments()
    {
        if (IsAudio)
        {
            var start = ((decimal)SourcePts * TimeBase.Numerator / TimeBase.Denominator).ToString(CultureInfo.InvariantCulture);
            var end = ((decimal)EndPts * TimeBase.Numerator / TimeBase.Denominator).ToString(CultureInfo.InvariantCulture);
            // Preserve the video's trim origin, including silence before late-starting audio.
            var filter = HasAudio
                ? $"[0:a:0]atrim=start={start}:end={end},asetpts=PTS-({start})/TB,aresample=48000:first_pts=0,apad=whole_len={AudioSampleCount},atrim=end_sample={AudioSampleCount}[a]"
                : $"anullsrc=r=48000:cl=mono,atrim=end_sample={AudioSampleCount}[a]";
            return [..InputArguments(), "-filter_complex", filter, "-map", "[a]", "-vn", "-sn", "-dn",
                "-ac", "1", "-ar", "48000", "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1"];
        }
        return [..InputArguments(), "-map", "0:v:0", "-an", "-sn", "-dn",
        "-vf", FormattableString.Invariant($"select=gte(pts\\,{SourcePts})") +
            (FrameEndPts is { } endPts ? FormattableString.Invariant($"*lt(pts\\,{endPts})") : "") +
            FormattableString.Invariant($",scale={Width}:{Height}:force_original_aspect_ratio=decrease,pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2:color=black"),
        "-frames:v", "1", "-fps_mode", "passthrough", "-threads", "1",
        "-c:v", "rawvideo", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"
        ];
    }
}
