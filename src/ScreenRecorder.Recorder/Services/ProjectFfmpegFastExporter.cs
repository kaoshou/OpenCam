// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Media.Projects;
using Serilog;

namespace ScreenRecorder.Recorder.Services;

public sealed partial class ProjectFfmpegExporter
{
    private async Task<RecordingExportResult?> TryFastExportAsync(IProjectHandle owner, RecordingProject snapshot,
        ProjectRenderPlan plan, BoundDirectory directory, string temporary, Guid exportId, Func<string, FileStream> create,
        HashSet<string> owned, IProgress<double> progress, CancellationToken ct)
    {
        if (process is not IProjectStreamingMediaProcess streaming || plan.Clips.Length > 128 ||
            !ProjectExportPlanner.HasOnlyWholeClips(plan)) return null;
        var sources = new List<FileStream>();
        var evidence = new List<ProjectSourceEvidence>();
        var watch = Stopwatch.StartNew();
        try
        {
            // Validate outside the fallback catch. Tampering/permission/cancellation must not start a render.
            foreach (var clip in plan.Clips)
            {
                ct.ThrowIfCancellationRequested();
                var source = ProjectPathPolicy.OpenSource(owner, clip.Source.RelativePath);
                sources.Add(source);
                await ValidateSource(source, clip.Source, ct);
                evidence.Add(await ProjectPacketInspector.InspectAsync(streaming, source, ct));
            }
            var decision = ProjectExportPlanner.Select(plan, evidence);
            Log.Information("Project export strategy {Strategy}; reason {Reason}; clips {Count}; inspect {ElapsedMs}ms",
                decision.Strategy, decision.Reason, evidence.Count, watch.ElapsedMilliseconds);
            // Until the continuous normalized-audio path is connected, use the proven exact renderer.
            // Never pass a multi-source job to the single-source remux or publish drifting AAC.
            if (decision.Strategy is ProjectExportStrategy.Render or ProjectExportStrategy.ConcatConvertAudio) return null;
            var totalBytes = sources.Sum(s => s.Length);
            var estimatedBytes = checked(totalBytes * 2 + (decision.Strategy == ProjectExportStrategy.ConvertAudioOnly ? plan.AudioSampleCount * 8 : 0) + 16 * 1024 * 1024);
            if (new DriveInfo(directory.CurrentPath).AvailableFreeSpace < estimatedBytes)
                throw new IOException("Insufficient space for MP4; originals remain saved.");
            progress.Report(.1);
            ct.ThrowIfCancellationRequested();
            var manifestName = temporary + ".concat";
            try
            {
                watch.Restart();
                await using (var output = create(temporary))
                {
                    if (decision.Strategy == ProjectExportStrategy.ConcatCopy)
                    {
                        await using (var manifest = create(manifestName))
                        {
                            await manifest.WriteAsync(Encoding.UTF8.GetBytes(ProjectMediaJob.CreateConcatManifest(evidence)), ct);
                            manifest.Flush(true);
                        }
                        await using (var manifest = directory.Read(manifestName))
                            await process.RunAsync(ProjectMediaJob.ConcatWholeRecordings(evidence, totalBytes), [manifest, ..sources], output, ct);
                        directory.DeleteOwnedFile(manifestName);
                        owned.Remove(manifestName);
                    }
                    else await process.RunAsync(ProjectMediaJob.RemuxWholeRecording(plan,
                        decision.Strategy == ProjectExportStrategy.ConvertAudioOnly, evidence[0].Audio is not null), sources, output, ct);
                    output.Flush(true);
                }
                Log.Information("Project {Strategy} completed in {ElapsedMs}ms", decision.Strategy, watch.ElapsedMilliseconds);
                progress.Report(.9);
                watch.Restart();
                await using (var output = directory.Read(temporary))
                    await ProjectRemuxVerifier.VerifyAsync(process, streaming, output, evidence,
                        decision.Strategy == ProjectExportStrategy.ConvertAudioOnly, ct);
                Log.Information("Project remux verification completed in {ElapsedMs}ms", watch.ElapsedMilliseconds);
                ct.ThrowIfCancellationRequested();
                var path = directory.PublishVerified(temporary, $"OpenCam_{ProjectNaming.FileStem(snapshot.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}_{exportId:N}.mp4");
                owned.Remove(temporary);
                progress.Report(1);
                return new(true, path, null);
            }
            catch (ProjectRemuxIncompatibleException ex)
            {
                Log.Information("Project remux verification requires rendering: {Reason}", ex.Message);
                if (owned.Contains(temporary)) { directory.DeleteOwnedFile(temporary); owned.Remove(temporary); }
                return null;
            }
        }
        finally { foreach (var source in sources) await source.DisposeAsync(); }
    }
}
