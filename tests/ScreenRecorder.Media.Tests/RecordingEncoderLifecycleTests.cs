// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.State;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Infrastructure.Recovery;
using ScreenRecorder.Infrastructure.Diagnostics;
using ScreenRecorder.Infrastructure.Storage;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.Encoders;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Remux;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EngineDeathDuringCommit_CannotPublishRecording(bool resume)
    {
        await using var scope = new RecordingScope();
        if (resume)
        {
            Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
            Assert.True((await scope.Recorder.PauseRecordingAsync()).Success);
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Store.OnSave = async session =>
        {
            if (session.SegmentFilePaths.Count != (resume ? 2 : 1)) return;
            scope.Store.OnSave = null;
            entered.SetResult();
            await release.Task;
        };
        async Task<bool> Start() => resume
            ? (await scope.Recorder.ResumeRecordingAsync()).Success
            : (await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success;
        var operation = Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scope.Factory.Created[^1].FailWhileRecording();
        release.SetResult();
        Assert.False(await operation);
        Assert.NotEqual(RecordingState.Recording, scope.Recorder.CurrentState);
        Assert.Equal(resume ? 2 : 1, scope.Recorder.CurrentSession!.SegmentFilePaths.Count);
        Assert.DoesNotContain(HardwareEncoderType.SoftwareCpu, scope.Factory.Requested);
        Assert.All(scope.Recorder.CurrentSession.SegmentFilePaths, file => Assert.True(File.Exists(file)));
    }

    [Fact]
    public async Task AudioLossCleanupFailure_IsContainedAndBlocksAnotherLaunch()
    {
        await using var scope = new RecordingScope();
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        scope.Factory.FailCleanup = true;
        var context = new ExceptionCatchingContext();
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            scope.Factory.Created[0].LoseAudio();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await UntilAsync(() => context.Error != null || scope.Store.LastSavedState == RecordingState.Failed);
        Assert.Null(context.Error);
        Assert.Equal(RecordingState.Failed, scope.Recorder.CurrentState);
        var saved = await scope.Store.LoadSessionAsync(scope.Recorder.CurrentSession!.WorkingDirectory);
        Assert.Equal(RecordingState.Failed, saved!.State);
        Assert.False((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.Single(scope.Factory.Created);
        Assert.Single(saved.SegmentFilePaths);
    }

    [Fact]
    public async Task ResumeCleanupFailure_ReturnsFailureAndPreservesEngineOwnership()
    {
        await using var scope = new RecordingScope();
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.True((await scope.Recorder.PauseRecordingAsync()).Success);
        scope.Factory.FailNext = scope.Factory.FailCleanup = true;
        Assert.False((await scope.Recorder.ResumeRecordingAsync()).Success);
        Assert.Equal(RecordingState.Failed, scope.Recorder.CurrentState);
        Assert.False((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.Equal(2, scope.Factory.Created.Count);
        Assert.Single(scope.Recorder.CurrentSession!.SegmentFilePaths);
    }

    [Fact]
    public async Task Recovery_ExcludesSafelyClosedFailedAttempts()
    {
        await using var scope = new RecordingScope();
        scope.Factory.FailNext = true;
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.True((await scope.Recorder.PauseRecordingAsync()).Success);
        var session = scope.Recorder.CurrentSession!;
        // The probe deliberately recognizes BOTH attempts as valid video.
        session.State = RecordingState.Interrupted;
        session.LastHeartbeatTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        await scope.Store.SaveSessionAsync(session);
        var recoveredRemux = new RemuxStub();
        var recovery = new RecordingRecoveryService(new JsonRecordingSessionStore(),
            recoveredRemux, new ProbeStub(), new StorageService());
        var result = await recovery.RecoverSessionAsync(session.WorkingDirectory);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Single(recoveredRemux.Inputs);
        Assert.Single(Directory.GetFiles(session.WorkingDirectory, "failed_attempt_*.mkv"));
    }

    private sealed class ExceptionCatchingContext : SynchronizationContext
    {
        public Exception? Error;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            try { callback(state); }
            catch (Exception ex) { Error = ex; }
        }
    }

    [Fact]
    public async Task MetadataFailureAfterHandshake_PreservesValidSegmentWithoutCpuRetry()
    {
        await using var scope = new RecordingScope();
        scope.Store.FailNextCommittedSave = true;
        Assert.False((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.Single(scope.Factory.Requested);
        Assert.Empty(scope.Selection.Failed);
        Assert.True(scope.Factory.Created[0].Disposed);
        var saved = await scope.Store.LoadSessionAsync(scope.Recorder.CurrentSession!.WorkingDirectory);
        Assert.Single(saved!.SegmentFilePaths);
        Assert.True(File.Exists(saved.SegmentFilePaths[0]));
        Assert.Equal(HardwareEncoderType.IntelQsv, saved.EncoderSelection!.Encoder);
        Assert.Equal(RecordingState.Failed, saved.State);
    }

    [Fact]
    public async Task CleanupFailure_PreventsFallbackAndAnotherSessionUntilResolved()
    {
        await using var scope = new RecordingScope();
        scope.Factory.FailNext = true;
        scope.Factory.FailCleanup = true;
        Assert.False((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.Single(scope.Factory.Requested);
        Assert.False((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.Single(scope.Factory.Requested);
        scope.Factory.FailCleanup = false;
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
    }

    [Fact]
    public async Task RuntimeFailure_IsNotSuppressedAsStartupFailure()
    {
        await using var scope = new RecordingScope();
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        scope.Factory.Created[0].FailWhileRecording();
        Assert.Equal(RecordingState.Failed, scope.Recorder.CurrentState);
        var saved = await scope.Store.LoadSessionAsync(scope.Recorder.CurrentSession!.WorkingDirectory);
        Assert.Equal(RecordingState.Failed, saved!.State);
        Assert.Single(saved.SegmentFilePaths);
        Assert.True(File.Exists(saved.WorkingFilePath));
    }

    [Fact]
    public async Task Startup_UsesSharedSelectionAndReportsActualEncoder()
    {
        await using var scope = new RecordingScope();
        scope.Factory.FailNext = true;
        scope.Factory.BeforeStart = () => Assert.Null(scope.Recorder.CurrentSession!.EncoderSelection);
        var start = await scope.Recorder.StartRecordingAsync(scope.Configuration);
        Assert.True(start.Success, start.ErrorMessage);
        Assert.Equal(new[] { HardwareEncoderType.IntelQsv, HardwareEncoderType.SoftwareCpu }, scope.Factory.Requested);
        Assert.Equal(new EncoderSelection(HardwareEncoderType.SoftwareCpu, EncoderFallbackReason.HardwareStartupFailed),
            scope.Recorder.CurrentSession!.EncoderSelection);
        Assert.Single(scope.Recorder.CurrentSession.SegmentFilePaths);
        Assert.Equal(HardwareEncoderType.Auto, scope.Configuration.EncoderType);
        Assert.Equal(new[] { HardwareEncoderType.IntelQsv }, scope.Selection.Failed);
        Assert.True(File.Exists(scope.Factory.Paths[1]));
        var diagnostic = Assert.Single(Directory.GetFiles(scope.Recorder.CurrentSession.WorkingDirectory, "failed_attempt_*.mkv"));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(diagnostic));
        Assert.NotEqual(scope.Factory.Paths[0], scope.Factory.Paths[1]);
    }

    [Fact]
    public async Task Cancellation_DoesNotStartCpuFallback()
    {
        await using var scope = new RecordingScope();
        using var cts = new CancellationTokenSource();
        scope.Factory.BeforeStart = cts.Cancel;
        var start = await scope.Recorder.StartRecordingAsync(scope.Configuration, cts.Token);
        Assert.False(start.Success);
        Assert.Single(scope.Factory.Requested);
        Assert.Empty(scope.Selection.Failed);
        Assert.All(scope.Factory.Created, engine => Assert.True(engine.Disposed));
        var persisted = await scope.Store.LoadSessionAsync(scope.Recorder.CurrentSession!.WorkingDirectory);
        Assert.Equal(RecordingState.Failed, persisted!.State);
    }

    [Fact]
    public async Task Resume_ReusesPinnedEncoderWithoutProbes()
    {
        await using var scope = new RecordingScope();
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.True((await scope.Recorder.PauseRecordingAsync()).Success);
        Assert.True(scope.Factory.Created[0].Disposed);
        Assert.True((await scope.Recorder.ResumeRecordingAsync()).Success);
        scope.Factory.Created[^1].LoseAudio();
        await UntilAsync(() => scope.Factory.Created.Count == 3 && scope.Recorder.CurrentSession!.SegmentFilePaths.Count == 3);
        Assert.Equal(1, scope.Selection.Selections);
        Assert.All(scope.Factory.Requested, encoder => Assert.Equal(HardwareEncoderType.IntelQsv, encoder));
        Assert.Equal(HardwareEncoderType.Auto, scope.Configuration.EncoderType);
        Assert.True(scope.Factory.Created[1].Disposed);
    }

    [Fact]
    public async Task ResumeFailure_PreservesPausedSessionAndSegments()
    {
        await using var scope = new RecordingScope();
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        var original = scope.Recorder.CurrentSession!.WorkingFilePath;
        var bytes = File.ReadAllBytes(original);
        Assert.True((await scope.Recorder.PauseRecordingAsync()).Success);
        scope.Factory.FailNext = true;
        Assert.False((await scope.Recorder.ResumeRecordingAsync()).Success);
        Assert.Equal(RecordingState.Paused, scope.Recorder.CurrentState);
        Assert.Equal(new[] { original }, scope.Recorder.CurrentSession!.SegmentFilePaths);
        Assert.Equal(original, scope.Recorder.CurrentSession.WorkingFilePath);
        Assert.Equal(bytes, File.ReadAllBytes(original));
        Assert.DoesNotContain(HardwareEncoderType.SoftwareCpu, scope.Factory.Requested);
        Assert.True(scope.Factory.Created[^1].Disposed);
        Assert.True((await scope.Recorder.ResumeRecordingAsync()).Success);
        Assert.Equal(3, scope.Factory.Paths.Distinct().Count());
        var stop = await scope.Recorder.StopRecordingAsync();
        Assert.True(stop.Success, stop.ErrorMessage);
        Assert.Equal(2, scope.Remuxer.Inputs.Count);
        Assert.DoesNotContain(scope.Factory.Paths[1], scope.Remuxer.Inputs);
    }

    [Fact]
    public async Task AudioLossRestartFailure_SafelyFinalizesExistingSegments()
    {
        await using var scope = new RecordingScope();
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        var original = scope.Recorder.CurrentSession!.WorkingFilePath;
        scope.Factory.FailNext = true;
        scope.Factory.Created[0].LoseAudio();
        await UntilAsync(() => scope.Recorder.CurrentState == RecordingState.Completed);
        Assert.Equal(new[] { original }, scope.Remuxer.Inputs);
        Assert.Equal(2, scope.Factory.Requested.Count);
        Assert.All(scope.Factory.Requested, encoder => Assert.Equal(HardwareEncoderType.IntelQsv, encoder));
        Assert.True(File.Exists(original));
    }

    [Fact]
    public async Task FailedAttemptPaths_AreNeverReusedOrOverwritten()
    {
        await using var scope = new RecordingScope();
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        Assert.True((await scope.Recorder.PauseRecordingAsync()).Success);
        var collision = Path.Combine(scope.Recorder.CurrentSession!.WorkingDirectory, "segment_001.mkv");
        await File.WriteAllTextAsync(collision, "pre-existing data");
        // Safely skipping or rejecting a collision is acceptable; no launch may target it.
        await scope.Recorder.ResumeRecordingAsync();
        Assert.Equal("pre-existing data", await File.ReadAllTextAsync(collision));
        Assert.DoesNotContain(collision, scope.Factory.Paths);
    }

    [Fact]
    public async Task PendingSegmentMetadata_IsSavedBeforeLaunch_WithoutChangingCommittedList()
    {
        await using var scope = new RecordingScope();
        Assert.True((await scope.Recorder.StartRecordingAsync(scope.Configuration)).Success);
        var original = scope.Recorder.CurrentSession!.WorkingFilePath;
        Assert.True((await scope.Recorder.PauseRecordingAsync()).Success);
        scope.Factory.BeforeStart = () =>
        {
            var persisted = JsonSerializer.Deserialize<RecordingSession>(File.ReadAllText(
                Path.Combine(scope.Recorder.CurrentSession.WorkingDirectory, "session.json")))!;
            Assert.Equal(new[] { original }, persisted.SegmentFilePaths);
            Assert.NotEqual(original, persisted.WorkingFilePath);
            Assert.Equal(HardwareEncoderType.IntelQsv, persisted.EncoderSelection!.Encoder);
        };
        Assert.True((await scope.Recorder.ResumeRecordingAsync()).Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealSyntheticSegments_KeepCodecAndRemuxDecodableOutput(bool windowsProvider)
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-encoder-integration-").FullName;
        try
        {
            var storage = new StorageService();
            var display = new FixedDisplay();
            var probe = new MediaFileProbe();
            await using var recorder = new RecordingOrchestrator(new RecordingStateMachine(), storage,
                new JsonRecordingSessionStore(), new DiskSpaceMonitor(storage), new StreamCopyRemuxer(), probe,
                display, windowsProvider ? new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider() : new MacOsFFmpegProvider(display))
                { UseSyntheticCaptureSource = true };
            var config = new RecordingConfiguration { OutputDirectory = root, AudioSource = AudioSourceType.None,
                EncoderType = HardwareEncoderType.SoftwareCpu, Fps = 30 };
            var start = await recorder.StartRecordingAsync(config);
            Assert.True(start.Success, start.ErrorMessage);
            await Task.Delay(350);
            Assert.True((await recorder.PauseRecordingAsync()).Success);
            Assert.True((await recorder.ResumeRecordingAsync()).Success);
            await Task.Delay(350);
            var paths = recorder.CurrentSession!.SegmentFilePaths.ToArray();
            var stop = await recorder.StopRecordingAsync();
            Assert.True(stop.Success, stop.ErrorMessage);
            var parts = await Task.WhenAll(paths.Select(p => probe.ProbeAsync(p)));
            Assert.Equal(2, parts.Length);
            Assert.All(parts, p => { Assert.Equal("h264", p.VideoCodec); Assert.Equal(320, p.Width); Assert.Equal(240, p.Height); Assert.Equal(1, p.AudioStreamCount); });
            var final = await probe.ProbeAsync(stop.FinalFilePath!);
            Assert.InRange(final.Duration.TotalSeconds, parts.Sum(p => p.Duration.TotalSeconds) - 0.2,
                parts.Sum(p => p.Duration.TotalSeconds) + 0.5);
            var info = new ProcessStartInfo(FFmpegDiscovery.FindFFmpegExecutable()!)
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-v", "error", "-i", stop.FinalFilePath!, "-f", "null", "-" }) info.ArgumentList.Add(arg);
            var decode = await new EncoderProbeRunner().RunAsync(info, TimeSpan.FromSeconds(15));
            Assert.Equal(EncoderProbeStatus.Available, decode.Status);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task UntilAsync(Func<bool> ready)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!ready()) await Task.Delay(10, limit.Token);
    }

    private sealed class RecordingScope : IAsyncDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("OpenCam-encoder-lifecycle-").FullName;
        public readonly SelectionStub Selection = new();
        public readonly EngineFactoryStub Factory = new();
        public readonly FaultableStore Store = new();
        public readonly RemuxStub Remuxer = new();
        public readonly HealthStub Health = new();
        public RecordingOrchestrator Recorder { get; }
        public RecordingConfiguration Configuration { get; }
        public RecordingScope(IDisplayService? suppliedDisplay = null)
        {
            var storage = new StorageService();
            var display = suppliedDisplay ?? new FixedDisplay();
            Recorder = new(new RecordingStateMachine(), storage, Store, new DiskSpaceMonitor(storage),
                Remuxer, new ProbeStub(), display, new MacOsFFmpegProvider(display),
                encoderSelectionService: Selection, engineFactory: Factory, captureHealthMonitor: Health);
            Configuration = new() { OutputDirectory = _root, AudioSource = AudioSourceType.None, EncoderType = HardwareEncoderType.Auto };
        }
        public async ValueTask DisposeAsync() { Factory.FailCleanup = false; await Recorder.DisposeAsync(); Directory.Delete(_root, true); }
    }

    private sealed class FaultableStore : IRecordingSessionStore
    {
        private readonly JsonRecordingSessionStore _inner = new();
        public bool FailNextCommittedSave;
        public Func<RecordingSession, Task>? OnSave;
        public RecordingState LastSavedState;
        public async Task SaveSessionAsync(RecordingSession session, CancellationToken token = default)
        {
            if (OnSave != null) await OnSave(session);
            if (FailNextCommittedSave && session.SegmentFilePaths.Count > 0)
            {
                FailNextCommittedSave = false;
                throw new IOException("injected post-handshake persistence failure");
            }
            await _inner.SaveSessionAsync(session, token);
            LastSavedState = session.State;
        }
        public Task<RecordingSession?> LoadSessionAsync(string directory, CancellationToken token = default) => _inner.LoadSessionAsync(directory, token);
        public Task<IReadOnlyList<RecordingSession>> FindAllSessionsAsync(string root, CancellationToken token = default) => _inner.FindAllSessionsAsync(root, token);
        public Task DeleteSessionAsync(string directory, CancellationToken token = default) => _inner.DeleteSessionAsync(directory, token);
    }

    private sealed class HealthStub : ICaptureHealthMonitor
    {
        public bool Healthy = true;
        public bool Check(CaptureSelection selection) => Healthy;
    }

    private sealed class SelectionStub : IEncoderSelectionService
    {
        public int Selections;
        public readonly List<HardwareEncoderType> Failed = new();
        public Task<EncoderSelection> SelectAsync(HardwareEncoderType preferred, CancellationToken cancellationToken = default)
        { Selections++; return Task.FromResult(new EncoderSelection(HardwareEncoderType.IntelQsv, EncoderFallbackReason.None)); }
        public void ReportStartupFailure(HardwareEncoderType encoder) => Failed.Add(encoder);
        public Task<IReadOnlyList<EncoderCapability>> DetectAvailableEncodersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<HardwareEncoderType> ResolveOptimalEncoderAsync(HardwareEncoderType preferred, CancellationToken cancellationToken = default) => (await SelectAsync(preferred, cancellationToken)).Encoder;
    }

    private sealed class EngineFactoryStub : IRecordingEngineFactory
    {
        public readonly List<HardwareEncoderType> Requested = new();
        public readonly List<string> Paths = new();
        public readonly List<EngineStub> Created = new();
        public bool FailNext;
        public bool FailCleanup;
        public Action? BeforeStart;
        public bool UseModern;
        public bool FailCapture;
        public readonly List<CaptureSelection?> Captures = new();
        public IScreenRecorderEngine Create(HardwareEncoderType? pinnedEncoder, CaptureSelection? pinnedCapture = null)
        {
            Captures.Add(pinnedCapture);
            Assert.NotNull(pinnedEncoder);
            Requested.Add(pinnedEncoder.Value);
            var engine = new EngineStub(pinnedEncoder.Value, this, FailNext, pinnedCapture);
            FailNext = false;
            Created.Add(engine);
            return engine;
        }
    }

    private sealed class EngineStub(HardwareEncoderType encoder, EngineFactoryStub owner, bool fail, CaptureSelection? pinned) : IScreenRecorderEngine
    {
        public bool IsRunning { get; private set; }
        public bool UseSyntheticCaptureSource { get; set; }
        public TimeSpan CurrentRecordedTime => TimeSpan.FromSeconds(1);
        public long CurrentFramesRecorded => 30;
        public HardwareEncoderType ActiveEncoder => encoder;
        public CaptureSelection? ActiveCapture { get; private set; }
        public bool Disposed;
        public event EventHandler<string>? EngineErrorOccurred;
        public event EventHandler<string>? EngineWarningOccurred { add { } remove { } }
        public event EventHandler? AudioDeviceLost;
        public void LoseAudio() => AudioDeviceLost?.Invoke(this, EventArgs.Empty);
        public void FailWhileRecording() { IsRunning = false; EngineErrorOccurred?.Invoke(this, "runtime failure"); }
        public Task StartRecordingAsync(string path, RecordingConfiguration config, CaptureRegion bounds, CancellationToken token = default)
        {
            owner.BeforeStart?.Invoke();
            token.ThrowIfCancellationRequested();
            owner.Paths.Add(path);
            using (var file = new FileStream(path, FileMode.CreateNew)) file.Write(new byte[] { 1, 2, 3, 4 });
            if (owner.UseModern)
            {
                ActiveCapture = pinned ?? new(CaptureBackend.DesktopDuplication, CaptureFallbackReason.None, "Test display", 7, 0, bounds);
                if (owner.FailCapture && ActiveCapture.Backend == CaptureBackend.DesktopDuplication)
                    throw new CaptureStartupException("injected DDA initialization failure");
            }
            if (fail)
            {
                EngineErrorOccurred?.Invoke(this, "injected startup failure");
                throw new EncoderStartupException("injected startup failure");
            }
            IsRunning = true;
            return Task.CompletedTask;
        }
        public Task StopRecordingAsync(CancellationToken token = default) { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync()
        {
            if (owner.FailCleanup) throw new IOException("injected cleanup failure");
            Disposed = true; IsRunning = false; return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedDisplay : IDisplayService
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => new[] { GetPrimaryMonitor() };
        public MonitorInfo GetPrimaryMonitor() => new(0, "Test display", new(0, 0, 320, 240), true, 1);
        public CaptureRegion GetVirtualScreenBounds() => GetPrimaryMonitor().Bounds;
    }

    private sealed class RemuxStub : IStreamCopyRemuxer
    {
        public IReadOnlyList<string> Inputs = Array.Empty<string>();
        public Task<bool> RemuxToMp4Async(string input, string output, IProgress<double>? progress = null, CancellationToken token = default) => ConcatAndRemuxToMp4Async(new[] { input }, output, progress, token);
        public Task<bool> ConcatAndRemuxToMp4Async(IReadOnlyList<string> inputs, string output, IProgress<double>? progress = null, CancellationToken token = default)
        { Inputs = inputs.ToArray(); File.WriteAllBytes(output, new byte[] { 1, 2, 3 }); return Task.FromResult(true); }
    }
    private sealed class ProbeStub : IMediaProbeService
    {
        public Task<MediaProbeResult> ProbeAsync(string file, CancellationToken token = default) =>
            Task.FromResult(new MediaProbeResult(true, "mp4", TimeSpan.FromSeconds(2), 3, 1, 1, "h264", 320, 240, 30, "aac", 48000, null));
    }
}
