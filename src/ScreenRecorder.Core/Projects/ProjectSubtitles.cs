// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;
namespace ScreenRecorder.Core.Projects;

public sealed record ProjectSubtitleCue(long StartTicks, long EndTicks, string Text);
public sealed record ProjectSubtitleTrack(Guid Id, string OriginalName, string Language,
    string RelativePath, long FileSize, string Sha256, string TimelineSignature);
public sealed record ProjectSubtitleSettings(ImmutableArray<ProjectSubtitleTrack> Tracks, Guid? DefaultTrackId)
{
    public static ProjectSubtitleSettings Empty { get; } = new([], null);
}

public static class ProjectSubtitleTimeline
{
    public static string ActiveText(IReadOnlyList<ProjectSubtitleCue> cues, long ticks) =>
        string.Join("\n", cues.Where(c => ticks >= c.StartTicks && ticks < c.EndTicks).Select(c => c.Text));
}
