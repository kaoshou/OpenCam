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
using ScreenRecorder.Platform.Windows;

namespace ScreenRecorder.Media.Tests;

public class PreflightTrialTests
{
    [Theory]
    [InlineData(AudioSourceType.None, false, false)]
    [InlineData(AudioSourceType.SystemOnly, false, true)]
    [InlineData(AudioSourceType.MicrophoneOnly, true, false)]
    [InlineData(AudioSourceType.SystemAndMicrophone, true, true)]
    public async Task TrialUsesCurrentEngineSamplesOnlyForSelectedInputs(AudioSourceType selection, bool microphone, bool system)
    {
        using var diagnostics = new PreflightTrialTestDiagnostics();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var root = Directory.CreateTempSubdirectory("OpenCam-trial-level-test-");
        var displays = new Displays();
        var executable = FFmpegDiscovery.FindFFmpegExecutable()!;
        var trialEngine = new TrialLevelEngine(displays);
        var session = new PreflightTrialSession(new(() => new(new FFmpegScreenRecorderEngine(
            new MacOsFFmpegProvider(displays), pinnedEncoder: HardwareEncoderType.SoftwareCpu) { UseSyntheticCaptureSource = true }, []), displays, executable),
            (_, _) => new(trialEngine, []), () => new(root.FullName), new(() => new TrialPipeMediaProcess(executable), () => new MeasuringAudio()));
        try
        {
            // Check startup and the trial transition are separate operations. The sample assertions
            // below need their own cancellation budget after the check has produced its first frame.
            using (var checking = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                try
                {
                    var startup = PrepareCheckAsync();
                    using (var startupProbe = new IndependentDelayProbe(startup,
                        observation => Serilog.Log.Debug("Independent task observation: {Observation}", observation), "preflight-check-setup"))
                        await startup;

                    async Task PrepareCheckAsync()
                    {
                        await session.StartAsync(new() { AudioSource = selection, Fps = 10 }, checking.Token);
                        Serilog.Log.Debug("Preflight test setup engine started: elapsedMs={ElapsedMs}", elapsed.Elapsed.TotalMilliseconds);
                        while (!session.ReadStatus(DateTimeOffset.UtcNow).HasFrame) await Task.Delay(30, checking.Token);
                        checking.Token.ThrowIfCancellationRequested();
                    }
                }
                catch (Exception error)
                {
                    throw new Xunit.Sdk.XunitException($"Check setup failed before trial: selection={selection}, " +
                        $"elapsedMs={elapsed.Elapsed.TotalMilliseconds:R}, phase={session.ReadStatus(DateTimeOffset.UtcNow).Phase}\n" +
                        $"{error}\n{diagnostics.Snapshot()}");
                }
            }
            var checkingReadyMs = elapsed.Elapsed.TotalMilliseconds;
            // Observe the trial transition independently of the scheduler under test.
            // Existing CI-only helper uses bounded dotnet-stack output, not dumps.
            using var observer = new DelayedHeartbeatStackObserver(() => Volatile.Read(ref trialEngine.Reads) == 0);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var recording = session.RecordTrialAsync(cancel.Token);
            PreflightStatus? status = null;
            double lastPollDelayMs = 0;
            try
            {
                do
                {
                    var poll = System.Diagnostics.Stopwatch.StartNew();
                    await Task.Delay(20, deadline.Token);
                    lastPollDelayMs = poll.Elapsed.TotalMilliseconds;
                    status = session.ReadStatus(DateTimeOffset.UtcNow);
                }
                while (trialEngine.Reads == 0);
                Assert.Equal(PreflightPhase.TrialRecording, status.Phase);
                Assert.Equal(microphone ? PreflightSourceState.Live : PreflightSourceState.Disabled, status.Microphone.State);
                Assert.Equal(system ? PreflightSourceState.Live : PreflightSourceState.Disabled, status.SystemAudio.State);
                Assert.Equal(microphone ? .65 : 0, status.Microphone.Rms);
                Assert.Equal(system ? .25 : 0, status.SystemAudio.Rms);
                trialEngine.SampleAge = TimeSpan.FromSeconds(2);
                status = session.ReadStatus(DateTimeOffset.UtcNow);
                Assert.Equal(microphone ? PreflightSourceState.Unavailable : PreflightSourceState.Disabled, status.Microphone.State);
                Assert.Equal(system ? PreflightSourceState.Unavailable : PreflightSourceState.Disabled, status.SystemAudio.State);
                trialEngine.NoSamples = true;
                status = session.ReadStatus(DateTimeOffset.UtcNow);
                Assert.Equal(microphone ? PreflightSourceState.Waiting : PreflightSourceState.Disabled, status.Microphone.State);
                Assert.Equal(system ? PreflightSourceState.Waiting : PreflightSourceState.Disabled, status.SystemAudio.State);
            }
            catch (Exception error)
            {
                ThreadPool.GetAvailableThreads(out var available, out _);
                throw new Xunit.Sdk.XunitException($"{error}\nTrial diagnostics before cleanup: selection={selection}, " +
                    $"elapsedMs={elapsed.Elapsed.TotalMilliseconds:R}, checkingReadyMs={checkingReadyMs:R}, " +
                    $"lastPollDelayMs={lastPollDelayMs:R}, task={recording.Status}, phase={status?.Phase}, " +
                    $"error={status?.ErrorCode}, reads={trialEngine.Reads}, running={trialEngine.IsRunning}, " +
                    $"frames={trialEngine.CurrentFramesRecorded}, seconds={trialEngine.CurrentRecordedTime.TotalSeconds:R}, " +
                    $"workers={ThreadPool.ThreadCount}, available={available}, pending={ThreadPool.PendingWorkItemCount}\n" +
                    diagnostics.Snapshot());
            }
            finally { cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recording); }
            var reads = trialEngine.Reads;
            session.ReadStatus(DateTimeOffset.UtcNow);
            Assert.Equal(reads, trialEngine.Reads); // No reads after cancellation/disposal.
        }
        finally { await session.DisposeAsync(); root.Delete(true); }
    }
    private sealed class TrialLevelEngine : FFmpegScreenRecorderEngine, IRecordingAudioLevelSource
    {
        public int Reads;
        public TimeSpan SampleAge;
        public bool NoSamples;
        public TrialLevelEngine(IDisplayService displays) : base(new MacOsFFmpegProvider(displays), pinnedEncoder: HardwareEncoderType.SoftwareCpu)
            => UseSyntheticCaptureSource = true;
        (AudioLevelSample? SystemAudio, AudioLevelSample? Microphone) IRecordingAudioLevelSource.ReadInputLevels(DateTimeOffset now)
        {
            Reads++;
            return NoSamples ? (null, null) : (new(.25, .4, now - SampleAge), new(.65, .8, now - SampleAge));
        }
    }
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FiveSecondSyntheticTrialStopsCheckingAndReplaysWithoutFormalProjectFiles(bool portablePipe)
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
                new ProjectPreviewDecoder(() => portablePipe || (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                    ? new TrialPipeMediaProcess(executable)
                    : OperatingSystem.IsWindows() ? new WindowsProjectMediaProcess(executable) : new MacProjectMediaProcess(executable), () => output)))
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
