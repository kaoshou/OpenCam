// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

/// <summary>Tracks only acknowledgements for the current project and submitted revision.</summary>
public sealed class ProjectSaveState(Guid projectId, long savedRevision)
{
    private long? _savingRevision;
    public long DirtyRevision { get; private set; } = savedRevision;
    public long SavedRevision { get; private set; } = savedRevision;
    public bool Saving => _savingRevision.HasValue;
    public bool IsDirty => DirtyRevision > SavedRevision;
    public string? Error { get; private set; }

    public void MarkDirty(long revision)
    {
        if (revision < DirtyRevision) throw new ArgumentOutOfRangeException(nameof(revision));
        DirtyRevision = revision;
    }

    public void BeginSave(long revision)
    {
        if (revision < SavedRevision || revision > DirtyRevision || Saving)
            throw new InvalidOperationException("Invalid or overlapping save.");
        _savingRevision = revision;
        Error = null;
    }

    public void Complete(ProjectSaveReceipt receipt)
    {
        if (receipt.ProjectId != projectId || receipt.Revision != _savingRevision) return;
        SavedRevision = receipt.Revision;
        _savingRevision = null;
        Error = null;
    }

    public void Fail(string error)
    {
        _savingRevision = null;
        Error = error;
    }
}
