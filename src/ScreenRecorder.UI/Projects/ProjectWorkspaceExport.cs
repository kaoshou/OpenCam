// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

public sealed record ProjectExportTarget(Guid ProjectId, long Revision, Guid? ClipId, string Name, long DurationTicks)
{
    public Guid? ServerInstanceId { get; init; }
}

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
        if (clipId is not Guid selected) return new(id, State.Revision, null, State.Name, DurationTicks)
            { ServerInstanceId = State.ServerInstanceId };
        var clip = Clips.FirstOrDefault(c => c.Id == selected);
        var span = TimelineClips.FirstOrDefault(c => c.ClipId == selected);
        return clip is null || span is null ? null : new(id, State.Revision, selected, clip.Name, span.EndTicks - span.StartTicks)
            { ServerInstanceId = State.ServerInstanceId };
    }

    private bool IsCurrentExportTarget(ProjectExportTarget target) => CanExport && !State.IsDirty && !HasPropertyDraft &&
        State.ProjectId == target.ProjectId && State.ServerInstanceId == target.ServerInstanceId &&
        State.Revision == target.Revision && (target.ClipId is not Guid clip || Clips.Any(c => c.Id == clip));

    public async Task ExportFileAsync(string path, ProjectExportTarget target, ProjectExportOptions options)
    {
        bool Current() => IsCurrentExportTarget(target) &&
            options.ClipId == target.ClipId && options.Scope == (target.ClipId is null ? ProjectExportScope.WholeProject : ProjectExportScope.Clip);
        if (!Current())
        { Error = Strings["ProjectExportTargetChanged"]; return; }
        await ExecuteAsync("ExportRecordingContent", new() { Path = path, PathIsExportFile = true, ExportOptions = options }, Current);
    }

    public async Task RequestExportAsync(ProjectExportFormat format, Guid? clipId,
        Func<ProjectExportTarget, Task<GifExportOptions?>> gifOptions,
        Func<ProjectExportTarget, ProjectExportFormat, Task<string?>> destination,
        Func<ProjectExportTarget, Task<Mp4ExportOptions?>>? mp4Options = null)
    {
        if (_choosingExport || !CanExport || !Enum.IsDefined(format)) return;
        _choosingExport = true;
        try
        {
            if (await PrepareExportAsync(clipId) is not { } target) return;
            GifExportOptions? gif = null;
            Mp4ExportOptions? mp4 = null;
            if (format == ProjectExportFormat.Gif && (gif = await gifOptions(target)) is null) return;
            if (format == ProjectExportFormat.Mp4 && mp4Options is not null &&
                (mp4 = await mp4Options(target)) is null) return;
            if (!IsCurrentExportTarget(target)) return;
            if (await destination(target, format) is not { } path) return;
            await ExportFileAsync(path, target, new(format,
                clipId is null ? ProjectExportScope.WholeProject : ProjectExportScope.Clip, clipId, gif, mp4));
        }
        finally { _choosingExport = false; }
    }

    public async Task RequestWebExportAsync(Func<ProjectExportTarget, Task<Mp4ExportOptions?>> options,
        Func<Task<string?>> destination)
    {
        if (_choosingExport || !CanExport) return;
        _choosingExport = true;
        try
        {
            if (await PrepareExportAsync(null) is not { } target) return;
            if (await options(target) is not { } selected || !IsCurrentExportTarget(target)) return;
            if (await destination() is not { } path || !IsCurrentExportTarget(target)) return;
            await ExportWebPlayerAsync(path, selected, target);
        }
        finally { _choosingExport = false; }
    }
}
