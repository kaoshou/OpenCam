// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;

namespace ScreenRecorder.Core.Projects;

/// <summary>In-memory edit history, separate from durable save acknowledgements.</summary>
public sealed class ProjectEditHistory
{
    private readonly Stack<ImmutableArray<ProjectClip>> _undo = new();
    private readonly Stack<ImmutableArray<ProjectClip>> _redo = new();
    public RecordingProject Current { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public ProjectEditHistory(RecordingProject project)
    {
        ProjectValidation.Validate(project);
        Current = project;
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
        _undo.Push(Current.Clips);
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
        _undo.Push(Current.Clips);
        _redo.Clear();
        Current = next;
    }

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);

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

    private static void AppendToHistory(Stack<ImmutableArray<ProjectClip>> stack, ImmutableArray<ProjectClip> additions)
    {
        var snapshots = stack.Reverse().Select(clips => clips.AddRange(additions)).ToArray();
        stack.Clear();
        foreach (var snapshot in snapshots) stack.Push(snapshot);
    }

    private void Restore(Stack<ImmutableArray<ProjectClip>> from, Stack<ImmutableArray<ProjectClip>> to)
    {
        if (from.Count == 0) return;
        var next = Current with { Revision = checked(Current.Revision + 1), Clips = from.Peek() };
        ProjectValidation.Validate(next);
        to.Push(Current.Clips);
        from.Pop();
        Current = next;
    }
}
