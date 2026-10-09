// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Infrastructure.Projects;
using System.Text.Json;
using Serilog;

namespace ScreenRecorder.Recorder.Services;

/// <summary>One still decoder, bounded memory, latest-position wins. Caller serializes queries with capture start.</summary>
public sealed class ProjectFrameService(ProjectRecordingCoordinator coordinator, IProjectMediaProcess process) : IAsyncDisposable
{
    private (Guid Project, long Revision, long Ticks, Guid Thumbnail, ProjectPreviewQuality Quality) _key;
    private Task<ProjectFrameReply>? _running;
    private CancellationTokenSource? _cancel;
    private bool _superseded;
    private ProjectFrameCache? _cache;
    private Guid _cacheProject;

    public async Task<ProjectFrameReply> QueryAsync(Guid projectId, long revision, long ticks, Guid thumbnailClipId = default,
        ProjectPreviewQuality quality = ProjectPreviewQuality.P720)
    {
        if (coordinator.Current?.ProjectId != projectId || coordinator.Current.Revision != revision ||
            coordinator.Mode is not (ProjectMode.Ready or ProjectMode.Paused))
            throw new InvalidOperationException("Preview snapshot is no longer current.");
        var snapshot = coordinator.Current;
        var selection = ProjectStillSelection.Create(snapshot, ticks, thumbnailClipId);
        var (plan, index, frame) = selection;
        var part = plan.Clips[index];
        var position = new ProjectPosition(part.Clip.Id, part.Source.Id, part.Clip.InPts);
        var key = (projectId, revision, ticks, thumbnailClipId, quality);
        var size = ProjectPreviewFormat.Resolve(snapshot.Canvas, quality);
        var width = thumbnailClipId == Guid.Empty ? size.Width : ProjectFrameReply.Width;
        var height = thumbnailClipId == Guid.Empty ? size.Height : ProjectFrameReply.Height;
        var byteCount = width * height * 4;
        ProjectFrameReply Pixels(byte[] rgba) => new(revision, ticks, position.ClipId, rgba)
            { PixelWidth = width, PixelHeight = height };
        if (_running is not null)
        {
            if (!_running.IsCompleted)
            {
                if (_key != key) { _superseded = true; _cancel!.Cancel(); }
                return new(revision, ticks, position.ClipId, null);
            }
            if (_key == key && !_superseded) return await _running;
            await _running;
            _cancel!.Dispose();
        }
        var lease = await coordinator.OpenMediaSourceAsync(projectId, revision, position.ClipId, default);
        if (_cacheProject != projectId) { _cache?.Dispose(); _cache = null; _cacheProject = projectId; }
        try { _cache ??= await coordinator.OpenFrameCacheAsync(projectId, default); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log.Debug(ex, "Frame cache unavailable; decoding without cache"); }
        _key = key;
        _superseded = false;
        _cancel = new(TimeSpan.FromSeconds(15));
        var token = _cancel.Token;
        _running = Task.Run(async () =>
        {
            using (lease)
            {
                try
                {
                    if (lease.Stream.Length != lease.Source.FileSize ||
                        !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(lease.Stream, token)),
                            lease.Source.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Original source fingerprint changed.");
                    lease.Stream.Position = 0;
                    // No revision/name in key: renaming can reuse identical pixels.
                    // Source fingerprint is rechecked above; cache never bypasses source validation.
                    var cacheKey = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
                        Algorithm = 3, lease.Source.Sha256, lease.Source.Timing, plan.Canvas, frame,
                        part.Clip.InPts, part.Clip.OutPts, part.Clip.Crop, part.Clip.Scale,
                        part.Clip.PositionX, part.Clip.PositionY,
                        StartNumerator = part.ExactStart.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StartDenominator = part.ExactStart.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        part.StartFrame,
                        Width = width, Height = height
                    })));
                    if (_cache is not null)
                    {
                        try {
                            var cached = await _cache.ReadAsync(cacheKey, byteCount, token);
                            if (cached is not null) return Pixels(cached);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { Log.Debug(ex, "Frame cache miss; rebuilding derived image"); }
                    }
                    var job = ProjectMediaJob.PreviewStill(plan, index, frame,
                        width, height);
                    var result = await process.RunAsync(job, [lease.Stream], null, token);
                    if (result.Output.Length != byteCount) throw new InvalidDataException("Invalid frame size.");
                    if (_cache is not null)
                        try { await _cache.PutAsync(cacheKey, result.Output, token); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { Log.Debug(ex, "Frame cache write skipped; source and preview are retained"); }
                    return Pixels(result.Output);
                }
                catch (Exception) { return new(revision, ticks, position.ClipId, null, "Preview could not be decoded. Original media is unchanged."); }
            }
        });
        return new(revision, ticks, position.ClipId, null);
    }

    public async Task SuspendAsync()
    {
        _cancel?.Cancel();
        if (_running is not null) await _running;
        _cancel?.Dispose();
        _cancel = null;
        _running = null;
        _cache?.Dispose(); _cache = null;
    }
    public async ValueTask DisposeAsync() => await SuspendAsync();
}
