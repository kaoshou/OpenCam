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

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);

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
