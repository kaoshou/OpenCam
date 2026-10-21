// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

public sealed record ProjectExportTarget(Guid ProjectId, long Revision, Guid? ClipId, string Name, long DurationTicks);

public sealed partial class ProjectWorkspaceViewModel
{
    private bool _choosingExport;

    public async Task<ProjectExportTarget?> PrepareExportAsync(Guid? clipId)
    {
        if (!CanExport) return null;
        var project = State.ProjectId;
        if (!await ResolveUnsavedAsync() || State.ProjectId != project) return null;
        await RefreshAsync();
        if (!CanExport || State.ProjectId != project || State.IsDirty || HasPropertyDraft || Error is not null ||
            project is not Guid id || TimelineClips.Count != Clips.Count || DurationTicks <= 0) return null;
        if (clipId is not Guid selected) return new(id, State.Revision, null, State.Name, DurationTicks);
        var clip = Clips.FirstOrDefault(c => c.Id == selected);
        var span = TimelineClips.FirstOrDefault(c => c.ClipId == selected);
        return clip is null || span is null ? null : new(id, State.Revision, selected, clip.Name, span.EndTicks - span.StartTicks);
    }

    public async Task ExportFileAsync(string path, ProjectExportTarget target, ProjectExportOptions options)
    {
        if (!CanExport || State.IsDirty || HasPropertyDraft || State.ProjectId != target.ProjectId ||
            State.Revision != target.Revision || target.ClipId is Guid clip && !Clips.Any(c => c.Id == clip) ||
            options.ClipId != target.ClipId || options.Scope != (target.ClipId is null ? ProjectExportScope.WholeProject : ProjectExportScope.Clip))
        { Error = Strings["ProjectExportTargetChanged"]; return; }
        await ExecuteAsync("ExportRecordingContent", new() { Path = path, PathIsExportFile = true, ExportOptions = options });
    }

    public async Task RequestExportAsync(ProjectExportFormat format, Guid? clipId,
        Func<ProjectExportTarget, Task<GifExportOptions?>> gifOptions,
        Func<ProjectExportTarget, ProjectExportFormat, Task<string?>> destination)
    {
        if (_choosingExport || !CanExport || !Enum.IsDefined(format)) return;
        _choosingExport = true;
        try
        {
            if (await PrepareExportAsync(clipId) is not { } target) return;
            GifExportOptions? gif = null;
            if (format == ProjectExportFormat.Gif && (gif = await gifOptions(target)) is null) return;
            if (await destination(target, format) is not { } path) return;
            await ExportFileAsync(path, target, new(format,
                clipId is null ? ProjectExportScope.WholeProject : ProjectExportScope.Clip, clipId, gif));
        }
        finally { _choosingExport = false; }
    }
}
