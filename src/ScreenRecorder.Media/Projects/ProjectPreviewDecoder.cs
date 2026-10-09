// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

public sealed record ProjectPreviewFrame(Guid ClipId, long TimelineTicks, byte[] Rgba);
public sealed class ProjectPreviewSettings(ProjectPreviewSize size)
{
    public ProjectPreviewSize Size { get; } = size;
    public PreviewAudioGate Audio { get; } = new();
}
public sealed class ProjectPreviewShutdownException(Exception cause)
    : IOException("Preview output shutdown could not be confirmed.", cause);

/// <summary>Bounded streaming preview using the export composition and the actual audio-device clock.</summary>
public sealed class ProjectPreviewDecoder(Func<IProjectStreamingMediaProcess> processFactory,
    Func<IProjectAudioOutput> audioFactory)
{
    public async Task PlayAsync(RecordingProject project, long startTicks,
        Func<Guid, CancellationToken, Task<FileStream>> openSource,
        Func<ProjectPreviewFrame, Task> publish, Action<long> position, CancellationToken ct)
        => await PlayConfiguredAsync(project, startTicks, new(ProjectPreviewFormat.Resolve(project.Canvas, ProjectPreviewQuality.P720)), openSource, publish, position, ct);

    public async Task PlayConfiguredAsync(RecordingProject project, long startTicks, ProjectPreviewSettings settings,
        Func<Guid, CancellationToken, Task<FileStream>> openSource,
        Func<ProjectPreviewFrame, Task> publish, Action<long> position, CancellationToken ct)
    {
        var audio = audioFactory();
        try { await PlayCoreAsync(project, startTicks, settings, openSource, publish, position, audio, ct); }
        finally
        {
            try { await audio.StopAsync(CancellationToken.None); await audio.DisposeAsync(); }
            catch (Exception ex) { throw new ProjectPreviewShutdownException(ex); }
        }
    }

    private async Task PlayCoreAsync(RecordingProject project, long startTicks, ProjectPreviewSettings settings,
        Func<Guid, CancellationToken, Task<FileStream>> openSource,
        Func<ProjectPreviewFrame, Task> publish, Action<long> position, IProjectAudioOutput audio, CancellationToken ct)
    {
        var plan = ProjectRenderPlan.Create(project);
        if (startTicks < 0 || startTicks >= plan.DurationTicks) throw new ArgumentOutOfRangeException(nameof(startTicks));
        using var life = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = life.Token;
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startSample = Ceiling(startTicks, 48000, TimeSpan.TicksPerSecond);
        var startFrame = Ceiling(startTicks, plan.Canvas.Fps.Numerator,
            checked(TimeSpan.TicksPerSecond * plan.Canvas.Fps.Denominator));
        await audio.StartAsync(token);
        async Task<FileStream> Open(ProjectRenderClip clip)
        {
            var stream = await openSource(clip.Clip.Id, token);
            try
            {
                if (stream.Length != clip.Source.FileSize || !Convert.ToHexString(await SHA256.HashDataAsync(stream, token))
                    .Equals(clip.Source.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Preview source changed.");
                stream.Position = 0;
                return stream;
            }
            catch { await stream.DisposeAsync(); throw; }
        }
        async Task Video()
        {
            var process = processFactory();
            for (var i = 0; i < plan.Clips.Length; i++)
            {
                var clip = plan.Clips[i];
                var frameNumber = Math.Max(startFrame, clip.StartFrame);
                if (frameNumber >= clip.EndFrame) continue;
                await using var source = await Open(clip);
                await process.RunStreamingAsync(ProjectMediaJob.PreviewVideo(plan, i, frameNumber - clip.StartFrame,
                    settings.Size.Width, settings.Size.Height), source,
                    async (stream, cancel) =>
                    {
                        while (frameNumber < clip.EndFrame)
                        {
                            var pixels = new byte[settings.Size.ByteCount];
                            await stream.ReadExactlyAsync(pixels, cancel);
                            firstFrame.TrySetResult();
                            var due = Ceiling(frameNumber, checked(48000 * plan.Canvas.Fps.Denominator), plan.Canvas.Fps.Numerator);
                            var deadline = DateTime.UtcNow.AddSeconds(30);
                            while (audio.PositionSamples < 0 || audio.PositionSamples + startSample < due)
                            {
                                if (DateTime.UtcNow > deadline) throw new IOException("Preview audio clock stopped.");
                                await Task.Delay(5, cancel);
                            }
                            cancel.ThrowIfCancellationRequested();
                            var ticks = Ceiling(frameNumber, checked(TimeSpan.TicksPerSecond * plan.Canvas.Fps.Denominator), plan.Canvas.Fps.Numerator);
                            await publish(new(clip.Clip.Id, Math.Max(startTicks, ticks), pixels));
                            frameNumber++;
                        }
                    }, token);
            }
            firstFrame.TrySetResult();
        }
        async Task Audio()
        {
            // Tiny trailing ranges can own audio samples but no output video frame.
            if (startFrame < plan.FrameCount) await firstFrame.Task.WaitAsync(token);
            var process = processFactory();
            long submitted = 0;
            for (var i = 0; i < plan.Clips.Length; i++)
            {
                var clip = plan.Clips[i];
                var sample = Math.Max(startSample, clip.StartAudioSample);
                if (sample >= clip.EndAudioSample) continue;
                await using var source = await Open(clip);
                await process.RunStreamingAsync(ProjectMediaJob.PreviewAudio(plan, i, sample - clip.StartAudioSample), source,
                    async (stream, cancel) =>
                    {
                        var block = new byte[8192];
                        var gated = new byte[8192];
                        var remaining = checked((clip.EndAudioSample - sample) * 8);
                        while (remaining > 0)
                        {
                            var count = (int)Math.Min(block.Length, remaining);
                            await stream.ReadExactlyAsync(block.AsMemory(0, count), cancel);
                            // Bound all queued output to ~100ms plus one PCM block, across platforms.
                            var deadline = DateTime.UtcNow.AddSeconds(30);
                            while (submitted - Math.Max(0, audio.PositionSamples) > 4800)
                            {
                                if (DateTime.UtcNow > deadline) throw new IOException("Preview audio clock stopped.");
                                await Task.Delay(5, cancel);
                            }
                            settings.Audio.Copy(block.AsSpan(0, count), gated.AsSpan(0, count));
                            await audio.WriteAsync(gated.AsMemory(0, count), cancel);
                            submitted += count / 8;
                            remaining -= count;
                        }
                    }, token);
            }
            await audio.CompleteAsync(token);
        }
        async Task Guard(Func<Task> work)
        {
            try { await work(); }
            catch { life.Cancel(); throw; }
        }
        var video = Guard(Video);
        var sound = Guard(Audio);
        try
        {
            while (!video.IsCompleted || !sound.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                if (audio.PositionSamples >= 0)
                    position(Math.Clamp(Ceiling(audio.PositionSamples + startSample, TimeSpan.TicksPerSecond, 48000), startTicks, plan.DurationTicks));
                await Task.Delay(20, token);
            }
            await Task.WhenAll(video, sound);
            position(plan.DurationTicks);
        }
        finally
        {
            life.Cancel();
            await Task.WhenAll(video, sound);
        }
    }

    private static long Ceiling(long value, long numerator, long denominator)
        => checked((long)(((BigInteger)value * numerator + denominator - 1) / denominator));
}
