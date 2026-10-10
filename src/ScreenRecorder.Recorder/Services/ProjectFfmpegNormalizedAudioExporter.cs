// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Media.Projects;
using Serilog;

namespace ScreenRecorder.Recorder.Services;

public sealed partial class ProjectFfmpegExporter
{
    private async Task<RecordingExportResult?> TryNormalizedAudioExportAsync(RecordingProject snapshot,
        ProjectRenderPlan plan, BoundDirectory directory, string temporary, Guid exportId,
        Func<string, FileStream> create, HashSet<string> owned, IProgress<double> progress,
        IProjectStreamingMediaProcess streaming, List<FileStream> sources,
        List<ProjectSourceEvidence> evidence, CancellationToken ct)
    {
        var totalBytes = sources.Sum(s => s.Length);
        var pcmBytes = checked(plan.AudioSampleCount * 8);
        // Source-sized video staging + final output + PCM and largest per-clip PCM staging.
        var estimated = checked(totalBytes * 4 + pcmBytes * 2 + 64L * 1024 * 1024);
        if (new DriveInfo(directory.CurrentPath).AvailableFreeSpace < estimated)
            throw new IOException("Insufficient space for normalized audio export; originals remain saved.");
        var videoName = temporary + ".video";
        var audioName = temporary + ".pcm";
        var partName = temporary + ".part";
        var manifestName = temporary + ".concat";
        var names = new[] { manifestName, videoName, audioName, partName, temporary };
        void Remove(string name)
        {
            if (!owned.Contains(name)) return;
            directory.DeleteOwnedFile(name);
            owned.Remove(name);
        }
        try
        {
            ReportPhase(progress, RecordingExportPhase.ConvertingAudio);
            await using (var manifest = create(manifestName))
            {
                await manifest.WriteAsync(Encoding.UTF8.GetBytes(ProjectMediaJob.CreateConcatManifest(evidence)), ct);
                manifest.Flush(true);
            }
            await using (var manifest = directory.Read(manifestName))
            await using (var video = create(videoName))
            {
                await process.RunAsync(ProjectMediaJob.ConcatWholeRecordings(evidence, totalBytes, videoOnly: true),
                    [manifest, ..sources], video, ct);
                video.Flush(true);
            }
            Remove(manifestName);
            progress.Report(.2);
            await using (var audio = create(audioName))
            {
                for (var i = 0; i < plan.Clips.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await using (var part = create(partName))
                        await process.RunAsync(ProjectMediaJob.RenderClipAudio(plan, i), [sources[i]], part, ct);
                    await using (var part = directory.Read(partName)) await part.CopyToAsync(audio, ct);
                    Remove(partName);
                    progress.Report(.2 + .6 * (i + 1) / plan.Clips.Length);
                }
                if (audio.Length != pcmBytes) throw new InvalidDataException("Normalized audio sample count mismatch.");
                audio.Flush(true);
            }
            await using (var video = directory.Read(videoName))
            await using (var audio = directory.Read(audioName))
            await using (var output = create(temporary))
            {
                await process.RunAsync(ProjectMediaJob.MuxCopiedVideoAndAudio(checked(video.Length + audio.Length + 64 * 1024 * 1024)),
                    [video, audio], output, ct);
                output.Flush(true);
            }
            progress.Report(.9);
            ReportPhase(progress, RecordingExportPhase.Verifying);
            await using (var output = directory.Read(temporary))
                await ProjectRemuxVerifier.VerifyAsync(process, streaming, output, evidence, true, ct,
                    normalizedAudioSamples: plan.AudioSampleCount);
            foreach (var name in names.Where(n => n != temporary)) Remove(name);
            ct.ThrowIfCancellationRequested();
            var path = directory.PublishVerified(temporary,
                $"OpenCam_{ProjectNaming.FileStem(snapshot.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}_{exportId:N}.mp4");
            owned.Remove(temporary);
            progress.Report(1);
            return new(true, path, null);
        }
        catch (ProjectRemuxIncompatibleException ex)
        {
            ct.ThrowIfCancellationRequested();
            Log.Information("Normalized audio export requires rendering: {Reason}", ex.Message);
            foreach (var name in names) Remove(name);
            return null;
        }
    }
}
