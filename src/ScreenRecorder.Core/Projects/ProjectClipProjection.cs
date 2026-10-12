using System.Collections.Immutable;

namespace ScreenRecorder.Core.Projects;

public static class ProjectClipProjection
{
    public static IEnumerable<ProjectClip> Leaves(ProjectClip clip) => clip.Parts.IsEmpty ? [clip] : clip.Parts;
    public static RecordingProject Expand(RecordingProject project) => project with {
        Clips = project.Clips.SelectMany(Leaves).Select(c => c with { GroupId = null }).ToImmutableArray()
    };
    public static Guid LogicalId(RecordingProject project, Guid leafId) =>
        project.Clips.First(c => c.Id == leafId || c.Parts.Any(p => p.Id == leafId)).Id;

    public static ProjectClip Combine(RecordingProject project, ProjectClip first, ProjectClip second)
    {
        if (first.GroupId != second.GroupId)
            throw new InvalidDataException("Cancel the affected groups before merging / 請先取消相關群組再合併。");
        var parts = Leaves(first).Concat(Leaves(second))
            .Select(c => c with { Id = Guid.NewGuid(), GroupId = null }).ToImmutableArray();
        return Wrap(project, first, parts);
    }

    internal static ProjectClip Wrap(RecordingProject project, ProjectClip parent, ImmutableArray<ProjectClip> parts)
    {
        if (parts.Length == 0) throw new InvalidDataException("Empty compound clip.");
        var duration = ProjectTimeline.Build(project with { Clips = parts }).DurationTicks;
        return parent with { SourceId = Guid.Empty, InPts = 0, OutPts = duration, Parts = parts,
            Volume = 1, Muted = false, FadeInTicks = 0, FadeOutTicks = 0, Crop = 0, Scale = 1, PositionX = 0, PositionY = 0 };
    }

    internal static ProjectClip Slice(RecordingProject project, ProjectClip clip, long start, long end)
    {
        var local = ProjectTimeline.Build(project with { Clips = clip.Parts });
        if (start < 0 || end <= start || end > local.DurationTicks) throw new InvalidDataException("Invalid compound range.");
        var kept = ImmutableArray.CreateBuilder<ProjectClip>();
        foreach (var s in local.Segments)
        {
            if (s.EndTicks <= start || s.StartTicks >= end) continue;
            var input = ProjectTimeline.SourcePts(s, start);
            var output = ProjectTimeline.SourcePts(s, end);
            if (output <= input) continue;
            kept.Add(ProjectClipEditor.WithRange(project, s.Clip, input, output) with { Id = Guid.NewGuid() });
        }
        return Wrap(project, clip, kept.ToImmutable());
    }
}
