// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;

namespace ScreenRecorder.Core.Projects;

/// <summary>In-memory edit history, separate from durable save acknowledgements.</summary>
public sealed class ProjectEditHistory
{
    private sealed record Snapshot(string Name, ImmutableArray<ProjectClip> Clips);
    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private Snapshot Capture() => new(Current.Name, Current.Clips);
    public RecordingProject Current { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public ProjectEditHistory(RecordingProject project)
    {
        ProjectValidation.Validate(project);
        Current = project;
    }

    public void RenameProject(string name)
    {
        ProjectValidation.ValidateName(name);
        if (Current.Name == name) return;
        var next = Current with { Name = name, Revision = checked(Current.Revision + 1) };
        ProjectValidation.Validate(next);
        _undo.Push(Capture());
        _redo.Clear();
        Current = next;
    }

    public void RenameClip(Guid clipId, string name)
    {
        ProjectValidation.ValidateName(name);
        var index = -1;
        for (var i = 0; i < Current.Clips.Length; i++)
            if (Current.Clips[i].Id == clipId) { index = i; break; }
        if (index < 0) throw new InvalidDataException("Clip not found.");
        if (Current.Clips[index].Name == name) return;
        var next = Current with {
            Revision = checked(Current.Revision + 1),
            Clips = Current.Clips.SetItem(index, Current.Clips[index] with { Name = name })
        };
        ProjectValidation.Validate(next);
        _undo.Push(Capture());
        _redo.Clear();
        Current = next;
    }

    public void Apply(ProjectClipEdit edit)
    {
        var clips = ProjectClipEditor.Apply(Current, edit);
        if (clips.SequenceEqual(Current.Clips)) return;
        var next = Current with { Revision = checked(Current.Revision + 1), Clips = clips };
        ProjectValidation.Validate(next);
        _ = ProjectTimeline.Build(next);
        _undo.Push(Capture());
        _redo.Clear();
        Current = next;
    }

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);

    public void AcceptSavedMetadata(RecordingProject project)
    {
        if (project.ProjectId != Current.ProjectId || !project.Clips.SequenceEqual(Current.Clips) || project.Name != Current.Name)
            throw new InvalidDataException("Saved metadata must preserve working edits.");
        ProjectValidation.Validate(project);
        Current = project;
    }

    public void ReplaceEdits(RecordingProject project, bool retainUndo)
    {
        if (project.ProjectId != Current.ProjectId || Current.Sources.Any(s => !project.Sources.Contains(s)))
            throw new InvalidDataException("Restoring edits must preserve original sources.");
        ProjectValidation.Validate(project);
        if (retainUndo) _undo.Push(Capture()); else _undo.Clear();
        _redo.Clear();
        Current = project;
    }

    /// <summary>Appending finalized recordings is not an edit of existing clips. Keep edit undo/redo intact.</summary>
    public void AcceptRecordingAppend(RecordingProject committed)
    {
        ProjectValidation.Validate(committed);
        if (committed.ProjectId != Current.ProjectId || committed.Revision < Current.Revision ||
            !committed.Clips.Take(Current.Clips.Length).SequenceEqual(Current.Clips) ||
            Current.Sources.Any(source => !committed.Sources.Contains(source)))
            throw new InvalidDataException("Recording append must preserve existing edits and sources.");
        var additions = committed.Clips.Skip(Current.Clips.Length).ToImmutableArray();
        AppendToHistory(_undo, additions);
        AppendToHistory(_redo, additions);
        Current = committed;
    }

    /// <summary>One insertion is one Undo; sources remain owned even when its clips are undone.</summary>
    public void AcceptRecordingInsertion(RecordingProject committed)
    {
        ProjectValidation.Validate(committed);
        if (committed.ProjectId != Current.ProjectId || committed.Revision != checked(Current.Revision + 1) ||
            committed.Sources.Length != Current.Sources.Length + 1 ||
            Current.Sources.Any(source => !committed.Sources.Contains(source)))
            throw new InvalidDataException("Insertion must preserve all existing original sources.");
        _undo.Push(Capture());
        _redo.Clear();
        Current = committed;
    }

    private static void AppendToHistory(Stack<Snapshot> stack, ImmutableArray<ProjectClip> additions)
    {
        var snapshots = stack.Reverse().Select(snapshot => snapshot with { Clips = snapshot.Clips.AddRange(additions) }).ToArray();
        stack.Clear();
        foreach (var snapshot in snapshots) stack.Push(snapshot);
    }

    private void Restore(Stack<Snapshot> from, Stack<Snapshot> to)
    {
        if (from.Count == 0) return;
        var next = Current with { Revision = checked(Current.Revision + 1), Name = from.Peek().Name, Clips = from.Peek().Clips };
        ProjectValidation.Validate(next);
        to.Push(Capture());
        from.Pop();
        Current = next;
    }
}
