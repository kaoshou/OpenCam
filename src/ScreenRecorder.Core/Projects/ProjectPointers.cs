// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public enum ProjectPointerStyle { Stick, Arrow, Dot, Hidden }
public sealed record ProjectPointerSettings(ProjectPointerStyle Style = ProjectPointerStyle.Stick, int SizePercent = 100);
public sealed record ProjectPointerTrack(string RelativePath, long FileSize, string Sha256,
    long VideoPtsOffsetMicroseconds, bool Complete);

public static class ProjectPointers
{
    public static bool HasVisiblePointer(RecordingProject project) =>
        project.PointerSettings.Style != ProjectPointerStyle.Hidden &&
        project.Clips.SelectMany(ProjectClipProjection.Leaves).Any(c =>
            project.Sources.Any(s => s.Id == c.SourceId && s.Pointer != null));

    public static void Validate(ProjectPointerSettings settings)
    {
        if (settings == null || !Enum.IsDefined(settings.Style) || settings.SizePercent is < 50 or > 200)
            throw new InvalidDataException("Invalid pointer style or size.");
    }
}
