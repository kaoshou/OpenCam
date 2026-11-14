// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Media.Projects;
using Serilog;

namespace ScreenRecorder.Recorder.Services;

/// <summary>Render a fixed revision into a new verified MP4. All media access stays bound to handles.</summary>
public sealed partial class ProjectFfmpegExporter(IProjectMediaProcess process, Mp4ExportOptions? options = null) : IRecordingContentExporter
{
    public async Task<RecordingExportResult> ExportAsync(IProjectHandle owner, RecordingProject snapshot,
        string outputDirectory, Guid exportId, IProgress<double> progress, CancellationToken ct)
    {
        using var timing = new ProjectExportTimingProgress(progress, exportId);
        progress = timing;
        ct.ThrowIfCancellationRequested();
        if (exportId == Guid.Empty || owner.Current.ProjectId != snapshot.ProjectId || owner.Current.Revision != snapshot.Revision)
            throw new InvalidOperationException("Export must use the owned, saved revision.");
        ProjectValidation.Validate(snapshot);
        var profile = options ?? new Mp4ExportOptions();
        profile.Validate();
        // This is an export-only view of the canvas; never save it to the project.
        var plan = ProjectRenderPlan.Create(snapshot with { Canvas = profile.OutputCanvas(snapshot.Canvas) });
        var hasPointer = ProjectPointerCompositor.IsRequired(plan);
        var basePlan = hasPointer ? ProjectRenderPlan.Create(snapshot with { Canvas = plan.Canvas,
            PointerSettings = new(ProjectPointerStyle.Hidden) }) : plan;
        using var pointer = new ProjectPointerCompositor(plan, (source, token) => PointerTrackCursor.OpenAsync(owner, source, token));
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
            ReportPhase(progress, RecordingExportPhase.Inspecting);
            var fast = profile.IsOriginal && !hasPointer
                ? await TryFastExportAsync(owner, snapshot, plan, directory, temporary, exportId, Create, owned, progress, ct)
                : null;
            if (fast is not null) return fast;
            // A failed fast-path verification may already have reported 90%.
            // Allocate actual rendering work to the remaining range rather than
            // letting the coordinator's monotonic gate hide all subsequent updates.
            var renderStart=timing.LastProgress;
            void ReportRender(double value)=>progress.Report(renderStart+(1-renderStart)*value);
            var drive = new DriveInfo(directory.CurrentPath);
            var estimated = checked(plan.AudioSampleCount * 16 + plan.DurationTicks / TimeSpan.TicksPerSecond * 2_000_000 + 64 * 1024 * 1024);
            if (drive.AvailableFreeSpace < estimated)
                throw new IOException("Insufficient space for lossless audio intermediates and the new MP4. Originals are unchanged.");
            await using (var video = Create(videoName))
            await using (var audio = Create(audioName))
            {
                long completedFrames=0,completedSamples=0;
                double Work(double frames,double samples)=>.72*frames/plan.FrameCount+.08*samples/Math.Max(1,plan.AudioSampleCount);
                for (var index = 0; index < plan.Clips.Length; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var clip = plan.Clips[index];
                    ReportPhase(progress,RecordingExportPhase.Inspecting);
                    await using var source = ProjectPathPolicy.OpenSource(owner, clip.Source.RelativePath);
                    await ValidateSource(source, clip.Source, ct);
                    ReportPhase(progress,RecordingExportPhase.Rendering);
                    var frames=clip.EndFrame-clip.StartFrame;var samples=clip.EndAudioSample-clip.StartAudioSample;
                    var videoProgress=new ExportFractionProgress(p=>ReportRender(Work(completedFrames+p*frames,completedSamples)));
                    var audioProgress=new ExportFractionProgress(p=>ReportRender(Work(completedFrames,completedSamples+p*samples)));
                    async Task Append(ProjectMediaJob job, FileStream target)
                    {
                        await using (var part = Create(partName))
                            await process.RunAsync(job, [source], part, ct);
                        await using (var part = directory.Read(partName)) await part.CopyToAsync(target, ct);
                        directory.DeleteOwnedFile(partName);
                        owned.Remove(partName);
                    }
                    if (clip.EndFrame > clip.StartFrame)
                    {
                        if (hasPointer && clip.Source.Pointer is not null)
                        {
                            if (process is not IProjectStreamingMediaProcess streaming)
                                throw new NotSupportedException("Editable pointer export requires streaming video support.");
                            await using (var part = Create(partName))
                            {
                                var frameBytes = checked(plan.Canvas.Width * plan.Canvas.Height * 4);
                                await new ProjectPointerVideoEncoder().EncodeAsync(plan.Canvas, profile.VideoCrf,
                                    clip.EndFrame - clip.StartFrame, part, (input, token) => streaming.RunStreamingAsync(
                                        ProjectMediaJob.PreviewVideo(basePlan, index, 0, plan.Canvas.Width, plan.Canvas.Height), source,
                                        async (decoded, decodeToken) =>
                                        {
                                            var pixels = new byte[frameBytes];
                                            for (var frame = clip.StartFrame; frame < clip.EndFrame; frame++)
                                            {
                                                await decoded.ReadExactlyAsync(pixels, decodeToken);
                                                await pointer.ApplyAsync(pixels, frame, plan.Canvas.Width, plan.Canvas.Height, ct: decodeToken);
                                                await input.WriteAsync(pixels, decodeToken);
                                                videoProgress.Report((double)(frame-clip.StartFrame+1)/frames);
                                            }
                                            if (await decoded.ReadAsync(new byte[1], decodeToken) != 0)
                                                throw new InvalidDataException("Unexpected compositor frames.");
                                        }, token), ct);
                            }
                            await using (var part = directory.Read(partName)) await part.CopyToAsync(video, ct);
                            directory.DeleteOwnedFile(partName); owned.Remove(partName);
                        }
                        else await Append(ProjectMediaJob.EncodeClipVideo(basePlan, index, options: profile).WithProgress(videoProgress,totalFrames:frames), video);
                    }
                    completedFrames+=frames;ReportRender(Work(completedFrames,completedSamples));
                    if (samples>0)await Append(ProjectMediaJob.RenderClipAudio(plan,index).WithProgress(audioProgress,totalAudioSamples:samples),audio);
                    completedSamples+=samples;ReportRender(Work(completedFrames,completedSamples));
                }
                video.Flush(true);
                audio.Flush(true);
            }
            await using (var video = directory.Read(videoName))
            await using (var audio = directory.Read(audioName))
            await using (var output = Create(temporary))
            {
                ReportPhase(progress,RecordingExportPhase.Packaging);
                await process.RunAsync(ProjectMediaJob.MuxEditedMp4(plan,
                    checked(video.Length + audio.Length + 64 * 1024 * 1024)).WithProgress(
                        new ExportFractionProgress(p=>ReportRender(.8+.1*p)),totalFrames:plan.FrameCount), [video, audio], output, ct);
                output.Flush(true);
            }
            ReportRender(0.9);
            ReportPhase(progress, RecordingExportPhase.Verifying);
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

    private static void ReportPhase(IProgress<double> progress, RecordingExportPhase phase)
        => (progress as IRecordingExportProgress)?.ReportPhase(phase);

    private sealed class ExportFractionProgress(Action<double> report):IProgress<double>
    {public void Report(double value)=>report(value);}
}
