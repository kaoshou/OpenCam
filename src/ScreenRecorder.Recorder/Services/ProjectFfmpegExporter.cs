// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Media.Projects;
using Serilog;

namespace ScreenRecorder.Recorder.Services;

/// <summary>Render a fixed revision into a new verified MP4. All media access stays bound to handles.</summary>
public sealed class ProjectFfmpegExporter(IProjectMediaProcess process) : IRecordingContentExporter
{
    public async Task<RecordingExportResult> ExportAsync(IProjectHandle owner, RecordingProject snapshot,
        string outputDirectory, Guid exportId, IProgress<double> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (exportId == Guid.Empty || owner.Current.ProjectId != snapshot.ProjectId || owner.Current.Revision != snapshot.Revision)
            throw new InvalidOperationException("Export must use the owned, saved revision.");
        var plan = ProjectRenderPlan.Create(snapshot);
        if (plan.FrameCount == 0) throw new InvalidOperationException("No retained video frames to export.");
        using var directory = BoundDirectory.Open(outputDirectory);
        var prefix = $".opencam-export-{Guid.NewGuid():N}";
        var videoName = prefix + ".h264";
        var audioName = prefix + ".pcm";
        var partName = prefix + ".part";
        var temporary = prefix + ".mp4";
        var owned = new HashSet<string>(StringComparer.Ordinal);
        FileStream Create(string name)
        {
            var stream = directory.CreateNew(name);
            owned.Add(name);
            return stream;
        }
        try
        {
            if (ProjectMediaJob.CopyWholeRecording(plan) is { } copyJob)
            {
                try
                {
                    await using (var source = ProjectPathPolicy.OpenSource(owner, plan.Clips[0].Source.RelativePath))
                    await using (var output = Create(temporary))
                    {
                        await ValidateSource(source, plan.Clips[0].Source, ct);
                        await process.RunAsync(copyJob, [source], output, ct);
                        output.Flush(true);
                    }
                    await using (var output = directory.Read(temporary))
                        await ProjectOutputVerifier.VerifyAsync(process, output, plan, ct);
                    ct.ThrowIfCancellationRequested();
                    var path = directory.PublishVerified(temporary, $"OpenCam_{ProjectNaming.FileStem(snapshot.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}_{exportId:N}.mp4");
                    owned.Remove(temporary);
                    progress.Report(1);
                    return new(true, path, null);
                }
                catch (InvalidDataException ex)
                {
                    // Metadata does not establish actual stream compatibility. Keep originals,
                    // discard only this attempt and use the validated normalization route.
                    Log.Information("Project remux was incompatible with the render plan: {Reason}", ex.Message);
                    if (owned.Remove(temporary)) directory.DeleteOwnedFile(temporary);
                }
            }
            var drive = new DriveInfo(directory.CurrentPath);
            var estimated = checked(plan.AudioSampleCount * 16 + plan.DurationTicks / TimeSpan.TicksPerSecond * 2_000_000 + 64 * 1024 * 1024);
            if (drive.AvailableFreeSpace < estimated)
                throw new IOException("Insufficient space for lossless audio intermediates and the new MP4. Originals are unchanged.");
            await using (var video = Create(videoName))
            await using (var audio = Create(audioName))
            {
                for (var index = 0; index < plan.Clips.Length; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var clip = plan.Clips[index];
                    await using var source = ProjectPathPolicy.OpenSource(owner, clip.Source.RelativePath);
                    await ValidateSource(source, clip.Source, ct);
                    async Task Append(ProjectMediaJob job, FileStream target)
                    {
                        await using (var part = Create(partName))
                            await process.RunAsync(job, [source], part, ct);
                        await using (var part = directory.Read(partName)) await part.CopyToAsync(target, ct);
                        directory.DeleteOwnedFile(partName);
                        owned.Remove(partName);
                    }
                    if (clip.EndFrame > clip.StartFrame) await Append(ProjectMediaJob.EncodeClipVideo(plan, index), video);
                    if (clip.EndAudioSample > clip.StartAudioSample) await Append(ProjectMediaJob.RenderClipAudio(plan, index), audio);
                    progress.Report(0.8 * (index + 1) / plan.Clips.Length);
                }
                video.Flush(true);
                audio.Flush(true);
            }
            await using (var video = directory.Read(videoName))
            await using (var audio = directory.Read(audioName))
            await using (var output = Create(temporary))
            {
                await process.RunAsync(ProjectMediaJob.MuxEditedMp4(plan,
                    checked(video.Length + audio.Length + 64 * 1024 * 1024)), [video, audio], output, ct);
                output.Flush(true);
            }
            progress.Report(0.9);
            await using (var output = directory.Read(temporary))
                await ProjectOutputVerifier.VerifyAsync(process, output, plan, ct);
            // Complete fallible intermediate cleanup before publishing the new final file.
            // A reported failure must not leave an apparently successful final MP4 behind.
            foreach (var name in owned.Where(name => name != temporary).ToArray())
            {
                directory.DeleteOwnedFile(name);
                owned.Remove(name);
            }
            ct.ThrowIfCancellationRequested();
            var final = directory.PublishVerified(temporary, $"OpenCam_{ProjectNaming.FileStem(snapshot.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}_{exportId:N}.mp4");
            owned.Remove(temporary);
            progress.Report(1);
            return new(true, final, null);
        }
        finally
        {
            foreach (var name in owned)
            {
                try { directory.DeleteOwnedFile(name); }
                catch (IOException ex) { Log.Warning(ex, "Could not remove owned export temporary {Name}", name); }
                catch (UnauthorizedAccessException ex) { Log.Warning(ex, "Could not remove owned export temporary {Name}", name); }
            }
        }
    }

    private static async Task ValidateSource(FileStream stream, ProjectSource source, CancellationToken ct)
    {
        if (stream.Length != source.FileSize || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)),
                source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original source fingerprint changed; export stopped.");
    }
}
