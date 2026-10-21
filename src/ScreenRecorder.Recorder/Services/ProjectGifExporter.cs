using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using System.Security.Cryptography;
using System.Text.Json;
namespace ScreenRecorder.Recorder.Services;
public sealed class ProjectGifExporter(IProjectMediaProcess process, GifExportOptions options) : IRecordingContentExporter
{
    public async Task<RecordingExportResult> ExportAsync(IProjectHandle owner, RecordingProject snapshot,
        string outputDirectory, Guid exportId, IProgress<double> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (exportId == Guid.Empty || owner.Current.ProjectId != snapshot.ProjectId || owner.Current.Revision != snapshot.Revision)
            throw new InvalidOperationException("Export must use the owned, saved revision.");
        var size = GifTiming.OutputSize(snapshot.Canvas, options);
        var plan = ProjectRenderPlan.Create(snapshot with { Canvas = snapshot.Canvas with { Fps = new(options.FramesPerSecond, 1) } });
        if (plan.FrameCount == 0) throw new InvalidDataException("No retained frames to export.");
        var rawBytes = checked(plan.FrameCount * size.Width * size.Height * 3);
        using var directory = BoundDirectory.Open(outputDirectory);
        if (new DriveInfo(directory.CurrentPath).AvailableFreeSpace < checked(rawBytes * 3 + 64 * 1024 * 1024))
            throw new IOException("Insufficient space for GIF intermediates. Originals are unchanged.");
        var prefix = $".opencam-gif-{exportId:N}-{Guid.NewGuid():N}";
        var rawName = prefix + ".rgb"; var partName = prefix + ".part";
        var paletteName = prefix + ".png"; var gifName = prefix + ".gif";
        var owned = new HashSet<string>(StringComparer.Ordinal);
        FileStream Create(string name) { var stream = directory.CreateNew(name); owned.Add(name); return stream; }
        void Delete(string name) { directory.DeleteOwnedFile(name); owned.Remove(name); }
        try
        {
            (progress as IRecordingExportProgress)?.ReportPhase(RecordingExportPhase.Rendering);
            await using (var raw = Create(rawName))
            {
                for (var index = 0; index < plan.Clips.Length; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var clip = plan.Clips[index];
                    if (clip.StartFrame == clip.EndFrame) continue;
                    await using var source = ProjectPathPolicy.OpenSource(owner, clip.Source.RelativePath);
                    if (source.Length != clip.Source.FileSize || !string.Equals(
                        Convert.ToHexString(await SHA256.HashDataAsync(source, ct)), clip.Source.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Original source fingerprint changed.");
                    await using (var part = Create(partName))
                        await process.RunAsync(ProjectMediaJob.RenderGifClip(plan, index, options), [source], part, ct);
                    await using (var part = directory.Read(partName)) await part.CopyToAsync(raw, ct);
                    Delete(partName);
                    progress.Report(.55 * (index + 1) / plan.Clips.Length);
                }
                if (raw.Length != rawBytes) throw new InvalidDataException("Incomplete GIF frame spool.");
                raw.Flush(true);
            }
            await using (var raw = directory.Read(rawName))
            await using (var palette = Create(paletteName))
            {
                await process.RunAsync(ProjectMediaJob.AnalyzeGifPalette(size.Width, size.Height, options.FramesPerSecond), [raw], palette, ct);
                palette.Flush(true);
            }
            progress.Report(.7);
            var finalDelay = GifTiming.FinalDelayCentiseconds(plan.DurationTicks, plan.FrameCount, options.FramesPerSecond);
            await using (var raw = directory.Read(rawName))
            await using (var palette = directory.Read(paletteName))
            await using (var gif = Create(gifName))
            {
                await process.RunAsync(ProjectMediaJob.EncodeGif(size.Width, size.Height, options.FramesPerSecond, finalDelay), [raw, palette], gif, ct);
                gif.Flush(true);
            }
            progress.Report(.9);
            (progress as IRecordingExportProgress)?.ReportPhase(RecordingExportPhase.VerifyingGif);
            await using (var gif = directory.Read(gifName))
            {
                var info = GifStructureReader.Read(gif, ct);
                var expectedCs = Math.Max(1, checked((long)(((System.Numerics.BigInteger)plan.DurationTicks + 50000) / 100000)));
                if (info.Width != size.Width || info.Height != size.Height || !info.InfiniteLoop ||
                    info.FrameCount != plan.FrameCount || Math.Abs(info.DurationCentiseconds - expectedCs) > 1)
                    throw new InvalidDataException("GIF does not match the selected timeline.");
                var result = await process.RunAsync(ProjectMediaJob.InspectGif(), [gif], null, ct);
                using var json = JsonDocument.Parse(result.Output);
                var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                if (streams.Length != 1 || streams[0].GetProperty("codec_name").GetString() != "gif" ||
                    streams[0].GetProperty("codec_type").GetString() != "video" ||
                    streams[0].GetProperty("width").GetInt32() != size.Width || streams[0].GetProperty("height").GetInt32() != size.Height ||
                    !long.TryParse(streams[0].GetProperty("nb_read_frames").GetString(), out var frames) || frames != plan.FrameCount)
                    throw new InvalidDataException("GIF could not be completely decoded.");
            }
            Delete(rawName); Delete(paletteName);
            ct.ThrowIfCancellationRequested();
            var final = directory.PublishVerified(gifName,
                $"OpenCam_{ProjectNaming.FileStem(snapshot.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}_{exportId:N}.gif");
            owned.Remove(gifName);
            progress.Report(1);
            return new(true, final, null);
        }
        finally
        {
            foreach (var name in owned)
                try { directory.DeleteOwnedFile(name); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Serilog.Log.Warning(ex, "Could not remove owned GIF temporary {Name}", name); }
        }
    }
}
