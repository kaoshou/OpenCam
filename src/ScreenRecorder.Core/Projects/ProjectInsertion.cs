// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public sealed record ProjectInsertionAnchor(Guid ProjectId, long Revision, long TimelineTicks, Guid? ClipId, long? SourcePts);

/// <summary>A single non-destructive transaction, applied only after a new source is verified.</summary>
public static class ProjectInsertion
{
    public static ProjectInsertionAnchor Anchor(RecordingProject project, long timelineTicks)
    {
        ProjectValidation.Validate(project);
        var timeline = ProjectTimeline.Build(project);
        if (timelineTicks < 0 || timelineTicks > timeline.DurationTicks)
            throw new InvalidDataException("Insertion is outside the retained timeline.");
        var point = timeline.Locate(timelineTicks);
        return new(project.ProjectId, project.Revision, timelineTicks, point?.ClipId, point?.SourcePts);
    }

    public static RecordingProject Apply(RecordingProject project, ProjectInsertionAnchor anchor, ProjectSource source, Guid operationId)
    {
        if (anchor != Anchor(project, anchor.TimelineTicks))
            throw new InvalidOperationException("Insertion anchor changed. New media is retained; no guessed insertion was applied.");
        if (operationId == Guid.Empty || project.Sources.Any(s => s.Id == source.Id) ||
            project.Clips.Any(c => c.Id == operationId || c.Id == source.Id))
            throw new InvalidDataException("Insertion identity is invalid or already committed.");
        var clips = project.Clips;
        var index = clips.Length;
        Guid? group = null;
        if (anchor.ClipId is { } id)
        {
            index = Array.FindIndex(clips.ToArray(), c => c.Id == id);
            var clip = clips[index];
            if (anchor.SourcePts > clip.InPts)
            {
                clips = ProjectClipEditor.Apply(project, new ProjectClipEdit.Split(id, anchor.SourcePts!.Value, operationId));
                index++;
                group = clip.GroupId;
            }
            else if (index > 0 && clips[index - 1].GroupId == clip.GroupId)
                group = clip.GroupId;
        }
        var inserted = new ProjectClip { Id = source.Id, SourceId = source.Id, Name = $"Clip {clips.Length + 1}",
            InPts = source.Timing.StartPts, OutPts = checked(source.Timing.StartPts + source.Timing.DurationTs), GroupId = group };
        var next = project with { Revision = checked(project.Revision + 1), Sources = project.Sources.Add(source),
            Sessions = project.Sessions.Contains(source.SessionId) ? project.Sessions : project.Sessions.Add(source.SessionId),
            Clips = clips.Insert(index, inserted) };
        ProjectValidation.Validate(next);
        _ = ProjectTimeline.Build(next);
        return next;
    }
}
