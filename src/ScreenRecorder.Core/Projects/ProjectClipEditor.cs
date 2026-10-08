// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;
using System.Numerics;

namespace ScreenRecorder.Core.Projects;

internal static class ProjectClipEditor
{
    internal static ImmutableArray<ProjectClip> Apply(RecordingProject project, ProjectClipEdit edit)
    {
        var clips = project.Clips.ToList();
        int Index(Guid id)
        {
            var index = clips.FindIndex(c => c.Id == id);
            return index >= 0 ? index : throw new InvalidDataException("Clip not found.");
        }
        switch (edit)
        {
            case ProjectClipEdit.Trim trim:
                var index = Index(trim.ClipId);
                clips[index] = WithRange(project, clips[index], trim.InPts, trim.OutPts);
                break;
            case ProjectClipEdit.Split split:
                index = Index(split.ClipId);
                var clip = clips[index];
                if (split.AtPts <= clip.InPts || split.AtPts >= clip.OutPts ||
                    split.RightClipId == Guid.Empty || clips.Any(c => c.Id == split.RightClipId))
                    throw new InvalidDataException("Invalid split boundary or clip identity.");
                clips[index] = WithRange(project, clip, clip.InPts, split.AtPts);
                clips.Insert(index + 1, WithRange(project, clip, split.AtPts, clip.OutPts) with { Id = split.RightClipId });
                break;
            case ProjectClipEdit.Remove remove:
                index = Index(remove.ClipId);
                clips.RemoveAt(index);
                break;
            case ProjectClipEdit.Move move:
                index = Index(move.ClipId);
                var groupId = clips[index].GroupId;
                var moving = clips.Where(c => groupId is null ? c.Id == move.ClipId : c.GroupId == groupId).ToList();
                if (move.BeforeClipId is Guid before)
                {
                    var destination = Index(before);
                    if (moving.Any(c => c.Id == before)) return project.Clips;
                    if (destination > 0 && clips[destination].GroupId is Guid targetGroup &&
                        clips[destination - 1].GroupId == targetGroup)
                        throw new InvalidDataException("Cannot insert inside another group.");
                }
                var movingIds = moving.Select(c => c.Id).ToHashSet();
                clips.RemoveAll(c => movingIds.Contains(c.Id));
                clips.InsertRange(move.BeforeClipId is Guid target ? Index(target) : clips.Count, moving);
                break;
            case ProjectClipEdit.Group group:
                var first = Index(group.FirstClipId);
                var last = Index(group.LastClipId);
                if (last <= first || group.GroupId == Guid.Empty)
                    throw new InvalidDataException("Select at least two adjacent clips.");
                var selected = clips.Skip(first).Take(last - first + 1).ToArray();
                var existingGroups = selected.Where(c => c.GroupId is not null).Select(c => c.GroupId).ToHashSet();
                if (clips.Where((_, i) => i < first || i > last)
                    .Any(c => c.GroupId == group.GroupId || (c.GroupId is not null && existingGroups.Contains(c.GroupId))))
                    throw new InvalidDataException("Cannot partially regroup an existing group.");
                for (var i = first; i <= last; i++) clips[i] = clips[i] with { GroupId = group.GroupId };
                break;
            case ProjectClipEdit.Ungroup ungroup:
                if (ungroup.GroupId == Guid.Empty || !clips.Any(c => c.GroupId == ungroup.GroupId))
                    throw new InvalidDataException("Group not found.");
                for (var i = 0; i < clips.Count; i++)
                    if (clips[i].GroupId == ungroup.GroupId) clips[i] = clips[i] with { GroupId = null };
                break;
            case ProjectClipEdit.RemoveRange range:
                var timeline = ProjectTimeline.Build(project);
                if (range.StartTicks < 0 || range.EndTicks <= range.StartTicks || range.EndTicks > timeline.DurationTicks)
                    throw new InvalidDataException("Invalid timeline selection.");
                clips.Clear();
                foreach (var segment in timeline.Segments)
                {
                    clip = segment.Clip;
                    if (segment.EndTicks <= range.StartTicks || segment.StartTicks >= range.EndTicks)
                    { clips.Add(clip); continue; }
                    var leftEnd = ProjectTimeline.SourcePts(segment, range.StartTicks);
                    var rightStart = ProjectTimeline.SourcePts(segment, range.EndTicks);
                    if (leftEnd == rightStart) { clips.Add(clip); continue; }
                    if (leftEnd > clip.InPts) clips.Add(WithRange(project, clip, clip.InPts, leftEnd));
                    if (rightStart < clip.OutPts)
                        clips.Add(WithRange(project, clip, rightStart, clip.OutPts) with {
                            Id = leftEnd > clip.InPts ? Guid.NewGuid() : clip.Id });
                }
                break;
            default: throw new InvalidDataException("Unknown clip edit.");
        }
        return clips.ToImmutableArray();
    }

    private static ProjectClip WithRange(RecordingProject project, ProjectClip clip, long input, long output)
    {
        var source = project.Sources.First(s => s.Id == clip.SourceId);
        if (input < source.Timing.StartPts || output <= input ||
            output > source.Timing.StartPts + source.Timing.DurationTs)
            throw new InvalidDataException("Clip outside source range.");
        var duration = ((BigInteger)output - input) * source.Timing.TimeBase.Numerator *
            TimeSpan.TicksPerSecond / source.Timing.TimeBase.Denominator;
        var bounded = (long)BigInteger.Min(duration, long.MaxValue);
        return clip with { InPts = input, OutPts = output,
            FadeInTicks = Math.Min(clip.FadeInTicks, bounded), FadeOutTicks = Math.Min(clip.FadeOutTicks, bounded) };
    }
}
