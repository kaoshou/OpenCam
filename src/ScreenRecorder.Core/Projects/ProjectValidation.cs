// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public static class ProjectValidation
{
    public const int MaximumManifestBytes = 4 * 1024 * 1024;
    public const int MaximumJsonDepth = 32;

    public static void Validate(RecordingProject p)
    {
        Require(p is not null, "Missing project.");
        Require(p.SchemaVersion == 1, "Unsupported project schema.");
        Require(p.ProjectId != Guid.Empty && p.Revision >= 0, "Invalid project identity.");
        Require(p.RecoveryDraftBaseRevision is null ||
            p.RecoveryDraftBaseRevision >= 0 && p.RecoveryDraftBaseRevision < p.Revision, "Invalid recovery draft base.");
        ValidateName(p.Name);
        Require(p.Canvas is not null && Dimension(p.Canvas.Width) && Dimension(p.Canvas.Height), "Invalid canvas.");
        Rational(p.Canvas.Fps);
        Require(!p.Sources.IsDefault && p.Sources.Length <= 10000 &&
            !p.Clips.IsDefault && p.Clips.Length <= 10000 &&
            !p.Sessions.IsDefault && p.Sessions.Length <= 1000, "Project collection limit exceeded.");
        Require(p.Sessions.Distinct(StringComparer.Ordinal).Count() == p.Sessions.Length, "Duplicate session.");
        foreach (var session in p.Sessions)
            Require(!string.IsNullOrWhiteSpace(session) && session.Length <= 200 &&
                session is not "." and not ".." && session.IndexOfAny(['/', '\\', ':', '\0', '\r', '\n']) < 0,
                "Invalid session identity.");

        var sources = new Dictionary<Guid, ProjectSource>();
        foreach (var s in p.Sources)
        {
            Require(s is not null && s.Id != Guid.Empty && sources.TryAdd(s.Id, s), "Invalid or duplicate source.");
            Require(p.Sessions.Contains(s.SessionId), "Source session missing.");
            Require(!string.IsNullOrWhiteSpace(s.RelativePath) && s.RelativePath.Length <= 1024, "Invalid source path.");
            Require(s.FileSize > 0 && s.Sha256 is { Length: 64 } && s.Sha256.All(Uri.IsHexDigit), "Invalid source fingerprint.");
            Require(Dimension(s.Width) && Dimension(s.Height), "Invalid source dimensions.");
            Require(!string.IsNullOrWhiteSpace(s.VideoCodec) && s.VideoCodec.Length <= 64 &&
                (s.AudioCodec is null || s.AudioCodec.Length <= 64), "Invalid codec metadata.");
            Require(s.Timing is not null, "Missing source timing.");
            Rational(s.Timing.TimeBase);
            Require(s.Timing.DurationTs > 0 &&
                s.Timing.StartPts <= long.MaxValue - s.Timing.DurationTs, "Invalid source duration.");
        }
        var ids = new HashSet<Guid>();
        var closedGroups = new HashSet<Guid>();
        Guid? previousGroup = null;
        foreach (var c in p.Clips)
        {
            Require(c is not null && c.Id != Guid.Empty && ids.Add(c.Id), "Invalid or duplicate clip.");
            Require(sources.TryGetValue(c.SourceId, out var s), "Clip source missing.");
            ValidateName(c.Name);
            Require(c.InPts >= s!.Timing.StartPts && c.OutPts > c.InPts &&
                c.OutPts <= s.Timing.StartPts + s.Timing.DurationTs, "Clip outside source range.");
            Require(c.GroupId != Guid.Empty, "Invalid group identity.");
            if (c.GroupId != previousGroup)
            {
                if (previousGroup is Guid finished) closedGroups.Add(finished);
                Require(c.GroupId is not Guid nextGroup || !closedGroups.Contains(nextGroup), "Group members must be adjacent.");
                previousGroup = c.GroupId;
            }
            Require(FiniteRange(c.Volume, 0, 2) && FiniteRange(c.Crop, 0, 40) &&
                FiniteRange(c.Scale, 1, 2) && FiniteRange(c.PositionX, -50, 50) &&
                FiniteRange(c.PositionY, -50, 50), "Invalid clip properties.");
            var durationSeconds = ((decimal)c.OutPts - c.InPts) * s.Timing.TimeBase.Numerator /
                s.Timing.TimeBase.Denominator;
            Require(c.FadeInTicks >= 0 && c.FadeOutTicks >= 0 &&
                (decimal)c.FadeInTicks / TimeSpan.TicksPerSecond <= durationSeconds &&
                (decimal)c.FadeOutTicks / TimeSpan.TicksPerSecond <= durationSeconds, "Invalid fade duration.");
        }
        var v = p.ViewState;
        Require(v is not null && v.PlayheadTicks >= 0 && FiniteRange(v.Zoom, 1, 8) &&
            FiniteRange(v.TimelineHeight, 190, 380), "Invalid view state.");
    }

    public static void ValidateName(string name) =>
        Require(!string.IsNullOrWhiteSpace(name) && name.Length <= 200 &&
            name.IndexOfAny(['\0', '\r', '\n']) < 0, "Invalid project or clip name.");

    private static bool Dimension(int value) => value is > 0 and <= 16384;
    private static bool FiniteRange(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;
    private static void Rational(ProjectRational r) =>
        Require(r is not null && r.Numerator is > 0 and <= int.MaxValue &&
            r.Denominator is > 0 and <= int.MaxValue, "Invalid media time base.");
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
