// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public static class ProjectExportSelection
{
    public static RecordingProject Create(RecordingProject snapshot, ProjectExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ProjectValidation.Validate(snapshot);
        if (!Enum.IsDefined(options.Format) || !Enum.IsDefined(options.Scope) ||
            (options.Format == ProjectExportFormat.Mp4 && options.Gif is not null) ||
            (options.Format == ProjectExportFormat.Gif && options.Mp4 is not null) ||
            (options.Gif is { } gif && (gif.MaximumWidth is not (null or 480 or 640 or 960) ||
                gif.FramesPerSecond is not (10 or 15 or 20))))
            throw new InvalidDataException("Invalid export format or GIF settings.");
        options.Mp4?.Validate();
        if (options.Scope == ProjectExportScope.WholeProject)
        {
            if (options.ClipId is not null) throw new InvalidDataException("Whole export cannot specify a clip.");
            return snapshot;
        }
        if (options.ClipId is not Guid id || id == Guid.Empty)
            throw new InvalidDataException("Select a valid clip to export.");
        // Only a top-level logical clip is selectable; compound leaf IDs never
        // silently expand into a different target.
        var selected = snapshot.Clips.SingleOrDefault(c => c.Id == id)
            ?? throw new InvalidDataException("The export clip no longer exists.");
        return snapshot with { Clips = [selected] };
    }
}
