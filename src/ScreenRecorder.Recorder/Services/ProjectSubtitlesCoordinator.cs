// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Collections.Immutable;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Projects;
namespace ScreenRecorder.Recorder.Services;

public sealed partial class ProjectRecordingCoordinator
{
    private RecordingProject? _subtitleSignatureProject;
    private string _subtitleSignature = "";
    public bool SubtitlesNeedSyncConfirmation
    {
        get
        {
            var p = Current;
            if (p is null || p.Subtitles.Tracks.IsEmpty) return false;
            if (!ReferenceEquals(p, _subtitleSignatureProject))
            { _subtitleSignature = ProjectSubtitleTimeline.Signature(p); _subtitleSignatureProject = p; }
            return p.Subtitles.Tracks.Any(t => t.TimelineSignature != _subtitleSignature);
        }
    }

    public async Task<ProjectSubtitleTrack> ImportSubtitleAsync(string path, string language, long revision,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            RequireNoExport(); CheckEditablePersistence(revision);
            if (_handle is null || HasRecoverableDraft) throw new InvalidOperationException("Resolve project recovery first.");
            var track = await ProjectSubtitleStore.ImportPathAsync(_handle, path, language, ct);
            return track with { TimelineSignature = ProjectSubtitleTimeline.Signature(Current!) };
        }
        finally { _gate.Release(); }
    }

    public Task<ProjectCommandResult> SetSubtitlesAsync(ProjectSubtitleSettings settings, long revision,
        CancellationToken ct = default, bool confirmSync = false) => EditAsync(revision, history =>
    {
        ProjectValidation.ValidateSubtitles(settings);
        foreach (var track in settings.Tracks)
            using (ProjectSubtitleStore.OpenRead(_handle!, track)) { }
        if (confirmSync)
        {
            var signature = ProjectSubtitleTimeline.Signature(history.Current);
            settings = settings with { Tracks = settings.Tracks.Select(t => t with { TimelineSignature = signature }).ToImmutableArray() };
        }
        history.SetSubtitles(settings);
    }, ct);

    public async Task<byte[]> ReadSubtitleDataAsync(Guid trackId, long revision, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            CheckEditablePersistence(revision);
            var track = Current!.Subtitles.Tracks.SingleOrDefault(t => t.Id == trackId)
                ?? throw new InvalidDataException("Subtitle is not attached to this project.");
            var bytes = await ProjectSubtitleStore.ReadBytesAsync(_handle!, track, ct);
            var payload = JsonSerializer.SerializeToUtf8Bytes(ProjectSubtitleParser.Parse(bytes, Path.GetExtension(track.RelativePath)));
            if (payload.Length > AuthenticatedMediaIpc.MaximumPixelBytes)
                throw new InvalidDataException("Parsed subtitle data exceeds the media transfer limit.");
            return payload;
        }
        finally { _gate.Release(); }
    }
}
