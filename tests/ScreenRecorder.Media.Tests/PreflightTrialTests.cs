using System.Security.Cryptography;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public class PreflightTrialTests
{
    [Fact]
    public async Task CancelTrialJoinsBothEnginesAndRemovesPrivateFile()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-preflight-cancel-test-");
        var engines = new List<FFmpegScreenRecorderEngine>();
        var displays = new Displays();
        var executable = FFmpegDiscovery.FindFFmpegExecutable()!;
        PreflightCaptureResources Create(HardwareEncoderType? encoder, CaptureSelection? capture)
        {
            Assert.All(engines, e => Assert.False(e.IsRunning));
            var engine = new FFmpegScreenRecorderEngine(new MacOsFFmpegProvider(displays), pinnedEncoder: HardwareEncoderType.SoftwareCpu)
                { UseSyntheticCaptureSource = true };
            engines.Add(engine); return new(engine, []);
        }
        try
        {
            await using (var session = new PreflightTrialSession(new(() => Create(null, null), displays, executable), Create,
                () => new(root.FullName), new(() => new MacProjectMediaProcess(executable), () => new MeasuringAudio())))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await session.StartAsync(new() { AudioSource = AudioSourceType.None, Fps = 10 }, timeout.Token);
                while (!session.ReadStatus(DateTimeOffset.UtcNow).HasFrame) await Task.Delay(50, timeout.Token);
                using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RecordTrialAsync(cancel.Token));
                Assert.All(engines, e => Assert.False(e.IsRunning));
                Assert.False(session.ReadStatus(DateTimeOffset.UtcNow).HasTrial);
            }
            Assert.Empty(root.EnumerateFileSystemInfos());
        }
        finally { root.Delete(true); }
    }
    [Fact]
    public async Task FiveSecondSyntheticTrialStopsCheckingAndReplaysWithoutFormalProjectFiles()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-preflight-trial-test-");
        var engines = new List<FFmpegScreenRecorderEngine>();
        var displays = new Displays();
        var executable = FFmpegDiscovery.FindFFmpegExecutable()!;
        PreflightCaptureResources Create(HardwareEncoderType? encoder, CaptureSelection? capture)
        {
            Assert.All(engines, e => Assert.False(e.IsRunning));
            var engine = new FFmpegScreenRecorderEngine(new MacOsFFmpegProvider(displays),
                pinnedEncoder: encoder ?? HardwareEncoderType.SoftwareCpu, pinnedCapture: capture) { UseSyntheticCaptureSource = true };
            engines.Add(engine);
            return new(engine, []);
        }
        var output = new MeasuringAudio();
        try
        {
            await using (var session = new PreflightTrialSession(
                new PreflightLiveSession(() => Create(null, null), displays, executable), Create,
                () => new PreflightTrialStorage(root.FullName),
                new ProjectPreviewDecoder(() => new MacProjectMediaProcess(executable), () => output)))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                await session.StartAsync(new() { AudioSource = AudioSourceType.SystemAndMicrophone, Fps = 10 }, timeout.Token);
                while (!session.ReadStatus(DateTimeOffset.UtcNow).HasFrame) await Task.Delay(50, timeout.Token);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await session.RecordTrialAsync(timeout.Token);
                Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(5));
                var status = session.ReadStatus(DateTimeOffset.UtcNow);
                Assert.True(status.HasTrial);
                Assert.Equal(PreflightPhase.Reviewing, status.Phase);
                Assert.InRange(status.TrialSeconds, 4.5, 7);
                Assert.Equal(PreflightSourceState.Waiting, status.Microphone.State);
                Assert.Equal(PreflightSourceState.Waiting, status.SystemAudio.State); // a silent track is not proof of live inputs
                Assert.All(engines, e => Assert.False(e.IsRunning));
                Assert.Single(Directory.GetFiles(root.FullName, "*", SearchOption.AllDirectories));
                await session.PlayTrialAsync(timeout.Token);
                while (session.ReadStatus(DateTimeOffset.UtcNow).Playing) await Task.Delay(30, timeout.Token);
                Assert.NotNull(session.ReadFrame());
                Assert.True(output.Bytes > 0);
                Assert.True(output.Stopped);
                await session.StopReplayAsync(timeout.Token);
            }
            Assert.Empty(root.EnumerateFileSystemInfos());
        }
        finally { root.Delete(true); }
    }

    private sealed class Displays : IDisplayService
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => [new(0, "synthetic", new(0, 0, 64, 36), true, 1)];
        public MonitorInfo? GetPrimaryMonitor() => GetMonitors()[0];
        public CaptureRegion GetVirtualScreenBounds() => new(0, 0, 64, 36);
    }
    private sealed class MeasuringAudio : IProjectAudioOutput
    {
        public long Bytes; public bool Stopped;
        public long PositionSamples => Interlocked.Read(ref Bytes) / 8;
        public Task StartAsync(CancellationToken ct) { Stopped = false; return Task.CompletedTask; }
        public Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct) { Interlocked.Add(ref Bytes, pcm.Length); return Task.CompletedTask; }
        public Task CompleteAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) { Stopped = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    [Fact]
    public async Task TrialStorageIsPrivateBoundedAndDeletesOnlyItsOwnedOutput()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-preflight-storage-test-");
        try
        {
            var unrelated = Path.Combine(root.FullName, "keep.txt");
            await File.WriteAllTextAsync(unrelated, "keep");
            var storage = new PreflightTrialStorage(root.FullName);
            var directory = storage.DirectoryPath;
            await storage.Writer.WriteAsync(new byte[] { 1, 2, 3, 4 });
            await storage.SealAsync(default);
            using (var read = storage.OpenRead()) Assert.Equal(4, read.Length);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4 })), storage.Sha256);
            await storage.DisposeAsync();
            Assert.False(Directory.Exists(directory));
            Assert.Equal("keep", await File.ReadAllTextAsync(unrelated));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task ReplacedTrialCannotBeReadOrDeletedAsOwnedData()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-preflight-replace-test-");
        try
        {
            var storage = new PreflightTrialStorage(root.FullName);
            await storage.Writer.WriteAsync(new byte[] { 1, 2, 3, 4 });
            await storage.SealAsync(default);
            var path = Assert.Single(Directory.GetFiles(storage.DirectoryPath));
            await File.WriteAllTextAsync(path, "foreign-data");
            Assert.Throws<InvalidDataException>(() => storage.OpenRead());
            await Assert.ThrowsAsync<InvalidDataException>(async () => await storage.DisposeAsync());
            Assert.Equal("foreign-data", await File.ReadAllTextAsync(path));
        }
        finally { root.Delete(true); }
    }
}
