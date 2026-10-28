// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    private bool _importBatch;
    private bool _cancelImportBatch;
    public bool IsImporting => _importBatch || State.VideoImport?.State == ProjectVideoImportState.Running;
    public bool CanImportVideo => CanEditTimeline && !HasPointerDraft && State.ProjectId is not null;
    public bool CanSaveProject => !IsBusy && !IsImporting;
    public bool HasImportStatus => State.VideoImport is not null;
    public string ImportStatusText => State.VideoImport is not { } import ? "" :
        Strings[import.State == ProjectVideoImportState.Running ? "VideoImport" + import.Phase :
            "VideoImport" + import.State] + " · " + import.Name +
        (import.Error is null ? "" : " · " + Strings[import.Error]);

    public async Task ImportVideosAsync(IEnumerable<string> paths)
    {
        if (!CanImportVideo) return;
        if (!await StopPreviewAsync()) return;
        var files = paths.ToArray();
        if (files.Length == 0) return;
        _importBatch = true;
        _cancelImportBatch = false;
        NotifyState();
        var failures = new List<string>();
        try
        {
            foreach (var path in files)
            {
                if (_cancelImportBatch || StatusUnconfirmed) break;
                await ExecuteAsync("ImportProjectVideo", new() { Path = path });
                if (StatusUnconfirmed) break;
                if (Error is not null) { failures.Add(Path.GetFileName(path) + ": " + Strings[Error]); continue; }
                var id = State.VideoImport?.Id;
                while (State.VideoImport is { State: ProjectVideoImportState.Running } status && status.Id == id)
                {
                    await Task.Delay(250);
                    await PollRecordingAsync();
                    if (StatusUnconfirmed) break;
                }
                if (State.VideoImport is { State: ProjectVideoImportState.Failed } failed)
                    failures.Add(failed.Name + ": " + Strings[failed.Error ?? "VideoImportInvalid"]);
                if (State.VideoImport is { State: ProjectVideoImportState.Succeeded, ClipId: { } clipId })
                    SelectedClip = Clips.FirstOrDefault(c => c.Id == clipId);
                if (State.VideoImport?.State == ProjectVideoImportState.Canceled) break;
            }
        }
        finally
        {
            _importBatch = false;
            if (failures.Count > 0) Error = string.Join(Environment.NewLine, failures);
            NotifyState();
        }
    }

    [RelayCommand]
    public async Task CancelVideoImportAsync()
    {
        _cancelImportBatch = true;
        if (State.VideoImport is { State: ProjectVideoImportState.Running } import)
            await ExecuteAsync("CancelProjectVideoImport", new() { ImportId = import.Id });
    }
}
