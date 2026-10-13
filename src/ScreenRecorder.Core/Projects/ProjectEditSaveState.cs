// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

/// <summary>Tracks content equality independently of the monotonic IPC revision.</summary>
public sealed class ProjectEditSaveState
{
    private RecordingProject _saved;
    public ProjectEditSaveState(RecordingProject saved)
    {
        ProjectValidation.Validate(saved);
        _saved = saved;
    }

    public bool IsDirty(RecordingProject working)
    {
        EnsureSameProject(working);
        return working.Name != _saved.Name || !working.Clips.SequenceEqual(_saved.Clips) ||
            working.Subtitles.DefaultTrackId != _saved.Subtitles.DefaultTrackId || !working.Subtitles.Tracks.SequenceEqual(_saved.Subtitles.Tracks);
    }

    public void AcceptSaved(RecordingProject saved)
    {
        EnsureSameProject(saved);
        ProjectValidation.Validate(saved);
        _saved = saved;
    }

    public RecordingProject DiscardEdits(RecordingProject working)
    {
        EnsureSameProject(working);
        var next = working with { Name = _saved.Name, Clips = _saved.Clips, Subtitles = _saved.Subtitles,
            SchemaVersion = _saved.SchemaVersion,
            Revision = checked(working.Revision + 1) };
        ProjectValidation.Validate(next);
        return next;
    }

    private void EnsureSameProject(RecordingProject project)
    {
        if (project.ProjectId != _saved.ProjectId)
            throw new InvalidDataException("Project identity changed.");
    }
}
