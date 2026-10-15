// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;
using System.Numerics;

namespace ScreenRecorder.Core.Projects;

public sealed record ProjectBookmark(Guid Id, string SessionId, string RelativePath, long OffsetTicks, string Name);
public sealed record ProjectBookmarkDocument(int SchemaVersion, Guid ProjectId, long Revision, ImmutableArray<ProjectBookmark> Bookmarks);
public sealed record ProjectBookmarkPosition(ProjectBookmark Bookmark, Guid? ClipId, long? TimelineTicks)
{
    public string Name => Bookmark.Name;
    public string TimeText => TimelineTicks is { } t ? TimeSpan.FromTicks(t).ToString(@"hh\:mm\:ss\.fff") : "—";
}

public static class ProjectBookmarks
{
    public static void Validate(ProjectBookmarkDocument document)
    {
        if (document.SchemaVersion != 1 || document.ProjectId == Guid.Empty || document.Revision < 0 ||
            document.Bookmarks.IsDefault || document.Bookmarks.Length > 10000)
            throw new InvalidDataException("Invalid bookmark document.");
        var ids = new HashSet<Guid>();
        foreach (var b in document.Bookmarks)
        {
            if (b is null || b.Id == Guid.Empty || !ids.Add(b.Id) || b.OffsetTicks < 0 ||
                string.IsNullOrWhiteSpace(b.SessionId) || b.SessionId.Length > 200 ||
                b.SessionId.IndexOfAny(['/', '\\', ':', '\0', '\r', '\n']) >= 0)
                throw new InvalidDataException("Invalid bookmark identity or time.");
            var path = b.RelativePath;
            if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 ||
                path.IndexOfAny(['\\', ':', '\0', '\r', '\n']) >= 0 ||
                !(path.StartsWith("sessions/", StringComparison.Ordinal) || path.StartsWith("sources/", StringComparison.Ordinal)) ||
                path.Split('/').Any(p => string.IsNullOrEmpty(p) || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ')) ||
                !path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid bookmark source path.");
            ProjectValidation.ValidateName(b.Name);
        }
    }

    public static IReadOnlyList<ProjectBookmarkPosition> Project(RecordingProject project, ProjectBookmark bookmark)
        => CreateProjector(project)(bookmark);

    public static Func<ProjectBookmark, IReadOnlyList<ProjectBookmarkPosition>> CreateProjector(RecordingProject project)
    {
        var sources = project.Sources.ToDictionary(s => (s.RelativePath, s.SessionId));
        var segments = ProjectTimeline.Build(ProjectClipProjection.Expand(project)).Segments.GroupBy(s => s.Clip.SourceId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var logical = project.Clips.SelectMany(c => ProjectClipProjection.Leaves(c).Select(leaf => (leaf.Id, Parent: c.Id)))
            .ToDictionary(p => p.Id, p => p.Parent);
        return bookmark =>
        {
        if (!sources.TryGetValue((bookmark.RelativePath, bookmark.SessionId), out var source)) return [];
        var tb = source.Timing.TimeBase;
        var delta = (BigInteger)bookmark.OffsetTicks * tb.Denominator / (TimeSpan.TicksPerSecond * (BigInteger)tb.Numerator);
        if (delta < 0 || delta >= source.Timing.DurationTs) return [];
        var pts = (BigInteger)source.Timing.StartPts + delta;
        var result = new List<ProjectBookmarkPosition>();
        foreach (var segment in segments.GetValueOrDefault(source.Id) ?? [])
        {
            if (segment.Clip.SourceId != source.Id || pts < segment.Clip.InPts || pts >= segment.Clip.OutPts) continue;
            var numerator = segment.ExactStartNumerator * tb.Denominator +
                (pts - segment.Clip.InPts) * tb.Numerator * TimeSpan.TicksPerSecond * segment.ExactStartDenominator;
            var denominator = segment.ExactStartDenominator * tb.Denominator;
            var ticks = (long)((numerator + denominator - 1) / denominator);
            result.Add(new(bookmark, logical[segment.Clip.Id], ticks));
        }
        return result;
        };
    }
}
