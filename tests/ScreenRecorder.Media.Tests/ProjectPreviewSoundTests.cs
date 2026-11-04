// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectPreviewSoundTests
{
    [DesktopPreviewTheory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(false, 1, 0)]
    [InlineData(false, 2, 0)]
    [InlineData(false, 0, 2500000)]
    [InlineData(false, 1, 2500000)]
    [InlineData(false, 0, 333668)]
    [InlineData(false, 0, 333334)]
    [InlineData(false, 0, 666667)]
    public async Task ActualDecodedSoundReachesOutputAndDeviceFailureIsNotHidden(bool outputFails, int muteMode, long endTicks)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-preview-sound-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "source.mkv");
            await RecordingContentExportIntegrationTests.Run(ffmpeg, ["-f", "lavfi", "-i", "color=blue:s=64x36:r=30:d=1",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1", "-c:v", "libx264", "-bf", "0", "-c:a", "aac", path]);
            await using var source = File.OpenRead(path);
            var info = await new ProjectSourceProbe().ProbeAsync(source);
            Assert.Equal("aac", info.AudioCodec);
            var id = Guid.NewGuid();
            var project = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "Sound", Sessions = ["s"],
                Canvas = new(64, 36, endTicks==333668?new(30000,1001):new(30, 1)), Sources = [new() { Id = id, SessionId = "s", RelativePath = "sources/a.mkv",
                    FileSize = source.Length, Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant(),
                    Width = info.Width, Height = info.Height, VideoCodec = info.VideoCodec, AudioCodec = info.AudioCodec, Timing = info.Timing }],
                Clips = [new() { Id = Guid.NewGuid(), SourceId = id, Name = "Clip", InPts = 100, OutPts = 800 }] };
            var backend = ProjectMediaBackend.Create(OperatingSystem.IsWindows() ? OSPlatform.Windows : OSPlatform.OSX, ffmpeg, "")!;
            var output = new MeasuringOutput(outputFails);
            double measuredPeak=0;
            var settings = new ProjectPreviewSettings(new(64,36)) { EndTicks=endTicks==0?null:endTicks,
                AudioLevel=(peak,_,_)=>measuredPeak=Math.Max(measuredPeak,peak) };
            settings.Audio.Muted = muteMode == 1;
            if (muteMode == 2) output.OnWrite = position => settings.Audio.Muted = position is >= 8192 and < 16384;
            var decoder = new ProjectPreviewDecoder(backend.CreateStreamingProcess, () => output);
            var frames=new List<ProjectPreviewFrame>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(endTicks==333668?3:15));
            Task Play() => decoder.PlayConfiguredAsync(project, endTicks==666667?333334:0, settings, (_, _) => Task.FromResult(File.OpenRead(path)),
                frame => {frames.Add(frame);return Task.CompletedTask;}, _ => { }, timeout.Token);
            if (outputFails)
            {
                var error = await Assert.ThrowsAsync<IOException>(Play);
                Assert.Contains("test audio device disconnected", error.Message);
            }
            else
            {
                await Play();
                if (muteMode == 1) Assert.Equal(0, output.Peak);
                else Assert.InRange(output.Peak, .01f, 1f);
                if (muteMode == 2)
                {
                    Assert.All(output.BlockPeaks.Skip(8).Take(8), peak => Assert.Equal(0, peak));
                    Assert.True(output.BlockPeaks.First() > .01f);
                    Assert.True(output.BlockPeaks.Last() > .01f);
                }
                Assert.Equal(endTicks switch {0=>33600,333668=>1601,333334=>1600,666667=>1599,_=>12000}, output.PositionSamples);
                if(endTicks>0)Assert.All(frames,frame=>Assert.True(frame.TimelineTicks<endTicks));
                if(endTicks is 333334 or 666667)Assert.Single(frames);
                Assert.InRange(measuredPeak,.01,1); // Also measured before preview-output mute.
            }
            Assert.False(project.Clips[0].Muted); Assert.Equal(1, project.Clips[0].Volume);
            Assert.Equal(project.Sources[0].Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant());
            Assert.True(output.Stopped);
        }
        finally { root.Delete(true); }
    }

    private sealed class MeasuringOutput(bool fail) : IProjectAudioOutput
    {
        private long position;
        public long PositionSamples => Interlocked.Read(ref position);
        public float Peak { get; private set; }
        public bool Stopped { get; private set; }
        public List<float> BlockPeaks { get; } = [];
        public Action<long>? OnWrite;
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct)
        {
            if (fail) throw new IOException("test audio device disconnected");
            float peak = 0;
            foreach (var sample in MemoryMarshal.Cast<byte, float>(pcm.Span)) peak = Math.Max(peak, Math.Abs(sample));
            Peak = Math.Max(Peak, peak); BlockPeaks.Add(peak);
            Interlocked.Add(ref position, pcm.Length / 8);
            OnWrite?.Invoke(PositionSamples);
            return Task.CompletedTask;
        }
        public Task CompleteAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) { Stopped = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class DesktopPreviewTheoryAttribute : TheoryAttribute
{
    public DesktopPreviewTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) Skip = "Requires Windows or macOS media backend.";
    }
}
