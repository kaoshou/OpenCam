using ScreenRecorder.Core.Enums;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Preflight;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public class PreflightAudioMediaTests
{
    [MacOsOnlyFact]
    public async Task TrialHasAudioAndNoVideoAndReplayUsesAudioOutputOnly()
    {
        var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var storage = new PreflightTrialStorage();
        await using (var capture = new FFmpegPreflightAudioCapture(new MacOsFFmpegProvider(), synthetic: true))
        {
            await capture.StartAsync(new() { AudioSource = AudioSourceType.SystemAndMicrophone }, storage.Writer, deadline.Token);
            await Task.Delay(TimeSpan.FromSeconds(5), deadline.Token);
            await capture.StopAsync(deadline.Token);
            Assert.Null(capture.ErrorCode);
        }
        await storage.SealAsync(deadline.Token);
        var probe = new PreflightAudioProbe(() => new MacProjectMediaProcess(ffmpeg),
            () => new MacProjectMediaProcess(ffmpeg));
        TimeSpan duration;
        await using (var input = storage.OpenRead())
            duration = await probe.ValidateAsync(input, deadline.Token);
        Assert.InRange(duration.TotalSeconds, 4.5, 10);
        var output = new Output();
        var playback = new PreflightAudioPlayback(storage.OpenRead, () => output,
            () => new MacProjectMediaProcess(ffmpeg), duration);
        await playback.PlayAsync(deadline.Token);
        while (playback.Playing) await Task.Delay(10, deadline.Token);
        Assert.Null(playback.ErrorCode);
        Assert.InRange(output.Bytes, 4.5 * 48000 * 8, 5 * 48000 * 8);
        Assert.True(output.NonZero);
        Assert.True(output.Stopped);
        Assert.True(output.Disposed);
        await playback.StopAsync(deadline.Token);
    }

    private sealed class Output : IProjectAudioOutput
    {
        public long Bytes;
        public bool NonZero, Stopped, Disposed;
        public long PositionSamples => Bytes / 8;
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct)
        {
            Assert.Equal(0, pcm.Length % 8);
            Bytes += pcm.Length;
            foreach (var value in pcm.Span) NonZero |= value != 0;
            return Task.CompletedTask;
        }
        public Task CompleteAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) { Stopped = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
