// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Windows.Display;
using ScreenRecorder.Platform.Windows.Audio;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.State;
using ScreenRecorder.Infrastructure.Diagnostics;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Infrastructure.Storage;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Remux;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Platform.macOS;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class ActualRecordingIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    private readonly Serilog.ILogger _previousLogger;
    private readonly Serilog.Core.Logger _diagnosticLogger;
    private readonly RecordingDiagnosticSink _diagnostics = new();

    public ActualRecordingIntegrationTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        // This assembly disables test parallelization. Preserve/restore the
        // logger so failed real-FFmpeg tests expose the bounded stderr tail
        // already recorded by the engine, not only the friendly UI error.
        _previousLogger = Serilog.Log.Logger;
        _diagnosticLogger = new Serilog.LoggerConfiguration().MinimumLevel.Debug()
            .WriteTo.Sink(_diagnostics).CreateLogger();
        Serilog.Log.Logger = _diagnosticLogger;
        _tempDir = Path.Combine(Path.GetTempPath(), "RealRecordingTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task PreflightOwnershipRejectsFormalStartBeforeCreatingSession()
    {
        var capture = new RecordingCaptureLease();
        await using var preflight = await capture.AcquireAsync(Guid.NewGuid(), default);
        await using var orchestrator = CreateSyntheticOrchestrator(capture);
        var result = await orchestrator.StartRecordingAsync(CreateSyntheticConfiguration(false));
        Assert.False(result.Success);
        Assert.Null(orchestrator.CurrentSession);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_tempDir));
    }

    [Fact]
    public async Task SyntheticCaptureDoesNotGenerateMediaFasterThanWallClock()
    {
        await using var orchestrator = CreateSyntheticOrchestrator();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var (started, startError, _) = await orchestrator.StartRecordingAsync(
            CreateSyntheticConfiguration(deleteWorkingFiles: false));
        Assert.True(started, startError);
        await Task.Delay(700);
        var (stopped, stopError, path) = await orchestrator.StopRecordingAsync();
        Assert.True(stopped, stopError);
        var media = await new MediaFileProbe().ProbeAsync(Assert.IsType<string>(path));
        Assert.True(media.IsValid);
        Assert.InRange(media.Duration.TotalSeconds, .1, elapsed.Elapsed.TotalSeconds + 2);
        var measurements = _diagnostics.Entries.Where(e => e.Contains("Recording performance: "))
            .Select(e => System.Text.Json.JsonDocument.Parse(e[(e.IndexOf("Recording performance: ", StringComparison.Ordinal) + 23)..])).ToArray();
        try
        {
            var stoppedMeasurement = Assert.Single(measurements, m => m.RootElement.GetProperty("Phase").GetString() == "Stopped");
            var metrics = stoppedMeasurement.RootElement.GetProperty("Metrics");
            Assert.True(metrics.GetProperty("Frames").GetInt64() > 0);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, metrics.GetProperty("OutputBytes").ValueKind);
            Assert.False(stoppedMeasurement.RootElement.GetProperty("Context").GetProperty("BoundOutput").GetBoolean());
            Assert.True(stoppedMeasurement.RootElement.GetProperty("Context").GetProperty("Synthetic").GetBoolean());
        }
        finally { foreach (var measurement in measurements) measurement.Dispose(); }
    }

    [Fact]
    public async Task BoundSyntheticOutputRetainsAllBytesWhileMeasuringWrites()
    {
        using var output = new MemoryStream();
        await using var engine = new FFmpegScreenRecorderEngine(new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider(),
            ffmpegPath: FFmpegDiscovery.FindFFmpegExecutable()) { UseSyntheticCaptureSource = true, OutputStream = output };
        await engine.StartRecordingAsync(Path.Combine(_tempDir, "bound.mkv"), new()
            { Fps = 60, AudioSource = AudioSourceType.None, EncoderType = HardwareEncoderType.SoftwareCpu }, new(0, 0, 320, 240));
        await Task.Delay(300);
        await engine.StopRecordingAsync();
        Assert.True(output.Length > 0);
        Assert.Equal(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }, output.ToArray()[..4]);
        var entry = Assert.Single(_diagnostics.Entries, e => e.Contains("Recording performance: ") && e.Contains("\"Phase\":\"Stopped\""));
        using var json = System.Text.Json.JsonDocument.Parse(entry[(entry.IndexOf("Recording performance: ", StringComparison.Ordinal) + 23)..]);
        Assert.Equal(output.Length, json.RootElement.GetProperty("Metrics").GetProperty("OutputBytes").GetInt64());
        Assert.True(json.RootElement.GetProperty("Context").GetProperty("BoundOutput").GetBoolean());
        Assert.Equal(60, json.RootElement.GetProperty("Context").GetProperty("RequestedFps").GetInt32());
    }

    [Fact]
    public async Task StartRecording_AppliesConfiguredDiskGuardThresholds()
    {
        var storageService = new StorageService();
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var displayService = new FixedDisplayService();
        await using var orchestrator = new RecordingOrchestrator(
            new RecordingStateMachine(),
            storageService,
            new JsonRecordingSessionStore(),
            diskMonitor,
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            displayService,
            new MacOsFFmpegProvider(displayService))
        {
            UseSyntheticCaptureSource = true
        };
        var config = CreateSyntheticConfiguration(deleteWorkingFiles: false);
        config.DiskWarningThresholdBytes = 5L * 1024 * 1024 * 1024;
        config.DiskCriticalThresholdBytes = 1000L * 1024 * 1024;

        var (startSuccess, startError, _) = await orchestrator.StartRecordingAsync(config);
        Assert.True(startSuccess, startError);
        try
        {
            Assert.Equal(config.DiskWarningThresholdBytes, diskMonitor.WarningThresholdBytes);
            Assert.Equal(config.DiskCriticalThresholdBytes, diskMonitor.CriticalThresholdBytes);
        }
        finally
        {
            await Task.Delay(500);
            await orchestrator.StopRecordingAsync();
        }
    }

    [Fact]
    public async Task StartRecording_InvalidDiskGuardThresholdsUseSafeDefaults()
    {
        var storageService = new StorageService();
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var displayService = new FixedDisplayService();
        await using var orchestrator = new RecordingOrchestrator(
            new RecordingStateMachine(),
            storageService,
            new JsonRecordingSessionStore(),
            diskMonitor,
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            displayService,
            new MacOsFFmpegProvider(displayService))
        {
            UseSyntheticCaptureSource = true
        };
        var config = CreateSyntheticConfiguration(deleteWorkingFiles: false);
        config.DiskWarningThresholdBytes = 400L * 1024 * 1024;
        config.DiskCriticalThresholdBytes = 500L * 1024 * 1024;

        var (startSuccess, startError, _) = await orchestrator.StartRecordingAsync(config);
        Assert.True(startSuccess, startError);
        try
        {
            Assert.Equal(
                RecordingConfiguration.DefaultDiskWarningThresholdBytes,
                diskMonitor.WarningThresholdBytes);
            Assert.Equal(
                RecordingConfiguration.DefaultDiskCriticalThresholdBytes,
                diskMonitor.CriticalThresholdBytes);
        }
        finally
        {
            await Task.Delay(500);
            await orchestrator.StopRecordingAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedRecording_RespectsWorkingFileCleanupPolicy(bool deleteWorkingFiles)
    {
        var storageService = new StorageService();
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var displayService = new FixedDisplayService();
        await using var orchestrator = new RecordingOrchestrator(
            new RecordingStateMachine(),
            storageService,
            new JsonRecordingSessionStore(),
            diskMonitor,
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            displayService,
            new MacOsFFmpegProvider(displayService))
        {
            UseSyntheticCaptureSource = true
        };

        var (startSuccess, startError, _) = await orchestrator.StartRecordingAsync(
            CreateSyntheticConfiguration(deleteWorkingFiles));
        Assert.True(startSuccess, startError);
        var segmentPaths = orchestrator.CurrentSession!.SegmentFilePaths.ToArray();

        await Task.Delay(1200);
        var (stopSuccess, stopError, finalPath) = await orchestrator.StopRecordingAsync();

        Assert.True(stopSuccess, stopError);
        Assert.True(File.Exists(finalPath));
        Assert.All(segmentPaths, path => Assert.Equal(!deleteWorkingFiles, File.Exists(path)));
    }

    [Fact]
    public async Task FailedMediaValidation_PreservesWorkingFilesWhenCleanupEnabled()
    {
        var storageService = new StorageService();
        var displayService = new FixedDisplayService();
        await using var orchestrator = new RecordingOrchestrator(
            new RecordingStateMachine(),
            storageService,
            new JsonRecordingSessionStore(),
            new DiskSpaceMonitor(storageService),
            new StreamCopyRemuxer(),
            new InvalidMediaProbe(),
            displayService,
            new MacOsFFmpegProvider(displayService))
        {
            UseSyntheticCaptureSource = true
        };

        var (startSuccess, startError, _) = await orchestrator.StartRecordingAsync(
            CreateSyntheticConfiguration(deleteWorkingFiles: true));
        Assert.True(startSuccess, startError);
        var segmentPaths = orchestrator.CurrentSession!.SegmentFilePaths.ToArray();

        await Task.Delay(1200);
        var (stopSuccess, _, _) = await orchestrator.StopRecordingAsync();

        Assert.False(stopSuccess);
        Assert.All(segmentPaths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task FailedRemux_PreservesWorkingFilesWhenCleanupEnabled()
    {
        var storageService = new StorageService();
        var displayService = new FixedDisplayService();
        await using var orchestrator = new RecordingOrchestrator(
            new RecordingStateMachine(),
            storageService,
            new JsonRecordingSessionStore(),
            new DiskSpaceMonitor(storageService),
            new FailingRemuxer(),
            new MediaFileProbe(),
            displayService,
            new MacOsFFmpegProvider(displayService))
        {
            UseSyntheticCaptureSource = true
        };

        var (startSuccess, startError, _) = await orchestrator.StartRecordingAsync(
            CreateSyntheticConfiguration(deleteWorkingFiles: true));
        Assert.True(startSuccess, startError);
        var segmentPaths = orchestrator.CurrentSession!.SegmentFilePaths.ToArray();

        await Task.Delay(1200);
        var (stopSuccess, _, _) = await orchestrator.StopRecordingAsync();

        Assert.False(stopSuccess);
        Assert.All(segmentPaths, path => Assert.True(File.Exists(path)));
    }

    [WindowsOnlyFact]
    public async Task RealScreenRecording_EndToEnd_ShouldRecordMkvAndRemuxToMp4()
    {
        var stateMachine = new RecordingStateMachine();
        var storageService = new StorageService();
        var sessionStore = new JsonRecordingSessionStore();
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var remuxer = new StreamCopyRemuxer();
        var probe = new MediaFileProbe();
        var displayService = new WindowsDisplayService();

        await using var orchestrator = new RecordingOrchestrator(
            stateMachine,
            storageService,
            sessionStore,
            diskMonitor,
            remuxer,
            probe,
            displayService, new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider())
        {
            UseSyntheticCaptureSource = true
        };

        // 測試以 640x480 小區域錄製 3 秒，避免大檔案負擔
        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            Region = new CaptureRegion(0, 0, 640, 480),
            Fps = 30,
            OutputDirectory = _tempDir,
            DeleteWorkingFileAfterSuccessfulRemux = false // 驗收原始 MKV 與 MP4 共存
        };

        // 1. 開始錄影
        var (startSuccess, startError, sessionId) = await orchestrator.StartRecordingAsync(config);
        Assert.True(startSuccess, $"啟動錄影失敗: {startError}");
        Assert.NotNull(sessionId);
        Assert.Equal(RecordingState.Recording, orchestrator.CurrentState);

        // 2. 錄製 3 秒
        await Task.Delay(3000);

        // 3. 檢查工作檔 MKV 是否已建立且正在持續寫入
        var workingMkv = orchestrator.CurrentSession?.WorkingFilePath;
        Assert.NotNull(workingMkv);
        Assert.True(File.Exists(workingMkv));
        var initialSize = new FileInfo(workingMkv!).Length;
        Assert.True(initialSize > 0, "MKV 檔案不應為 0 bytes");

        // 4. 正常停止錄影
        var (stopSuccess, stopError, finalMp4) = await orchestrator.StopRecordingAsync();
        Assert.True(stopSuccess, $"停止錄影或 Remux 失敗: {stopError}");
        Assert.NotNull(finalMp4);
        Assert.Equal(RecordingState.Completed, orchestrator.CurrentState);

        // 5. 驗證產生的 MP4 與原始 MKV
        Assert.True(File.Exists(finalMp4));
        Assert.True(File.Exists(workingMkv));

        // 6. 使用 ffprobe 驗證成品 MP4
        var probeResult = await probe.ProbeAsync(finalMp4!);
        Assert.True(probeResult.IsValid, "MP4 探針檢驗無效");
        Assert.Equal(1, probeResult.VideoStreamCount);
        Assert.Equal(640, probeResult.Width);
        Assert.Equal(480, probeResult.Height);
        Assert.True(probeResult.Duration.TotalSeconds >= 1.5, $"錄影時長異常: {probeResult.Duration.TotalSeconds}s");
    }

    [Fact]
    public async Task SessionHeartbeat_ContinuesWhileRecordingAndPaused()
    {
        var stateMachine = new RecordingStateMachine();
        var storageService = new StorageService();
        var sessionStore = new FailOnceRecordingSessionStore();
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var remuxer = new StreamCopyRemuxer();
        var probe = new MediaFileProbe();
        var displayService = new FixedDisplayService();

        await using var orchestrator = new RecordingOrchestrator(
            stateMachine,
            storageService,
            sessionStore,
            diskMonitor,
            remuxer,
            probe,
            displayService,
            new MacOsFFmpegProvider(displayService))
        {
            UseSyntheticCaptureSource = true
        };

        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            Region = new CaptureRegion(0, 0, 320, 240),
            Fps = 30,
            AudioSource = AudioSourceType.None,
            EncoderType = HardwareEncoderType.SoftwareCpu,
            OutputDirectory = _tempDir,
            DeleteWorkingFileAfterSuccessfulRemux = false
        };

        var (startSuccess, startError, _) = await orchestrator.StartRecordingAsync(config);
        Assert.True(startSuccess, startError);
        var sessionDirectory = Assert.IsType<string>(orchestrator.CurrentSession?.WorkingDirectory);
        var started = await sessionStore.LoadSessionAsync(sessionDirectory);
        Assert.NotNull(started);

        using var stackObserver = new DelayedHeartbeatStackObserver(() =>
            sessionStore.LastPersistedHeartbeatTicks <= started.LastHeartbeatTime.UtcTicks);
        await WaitForDurableHeartbeatAsync(started.LastHeartbeatTime);
        var whileRecording = await sessionStore.LoadSessionAsync(sessionDirectory);
        Assert.NotNull(whileRecording);
        Assert.True(whileRecording.LastHeartbeatTime > started.LastHeartbeatTime,
            $"Durable heartbeat did not advance; memory={orchestrator.CurrentSession?.LastHeartbeatTime:O}, " +
            $"persisted={whileRecording.LastHeartbeatTime:O}; store events before cleanup:\n{string.Join("\n", sessionStore.Events)}\n" +
            $"Scheduling diagnostics before cleanup:\n{string.Join("\n", _diagnostics.Entries)}");

        var (pauseSuccess, pauseError) = await orchestrator.PauseRecordingAsync();
        Assert.True(pauseSuccess, pauseError);
        var paused = await sessionStore.LoadSessionAsync(sessionDirectory);
        Assert.NotNull(paused);

        await WaitForDurableHeartbeatAsync(paused.LastHeartbeatTime);
        var whilePaused = await sessionStore.LoadSessionAsync(sessionDirectory);
        Assert.NotNull(whilePaused);
        Assert.True(whilePaused.LastHeartbeatTime > paused.LastHeartbeatTime);

        var (stopSuccess, stopError, _) = await orchestrator.StopRecordingAsync();
        Assert.True(stopSuccess, stopError);

        async Task WaitForDurableHeartbeatAsync(DateTimeOffset previous)
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await sessionStore.WaitForHeartbeatAfterAsync(previous, deadline.Token);
            }
            catch (OperationCanceledException)
            {
                throw new Xunit.Sdk.XunitException($"Durable heartbeat did not complete; memory={orchestrator.CurrentSession?.LastHeartbeatTime:O}; " +
                    $"store events before cleanup:\n{string.Join("\n", sessionStore.Events)}\n" +
                    $"Scheduling diagnostics before cleanup:\n{string.Join("\n", _diagnostics.Entries)}");
            }
        }
    }

    [Fact]
    public async Task SyntheticRecording_TelemetryTransitionsFromUnknownToHealthy()
    {
        await using var orchestrator = CreateSyntheticOrchestrator();
        var (startSuccess, startError, _) = await orchestrator.StartRecordingAsync(
            CreateSyntheticConfiguration(deleteWorkingFiles: false));
        Assert.True(startSuccess, startError);

        var beforeObservation = orchestrator.GetTelemetry();
        Assert.Null(beforeObservation.IsVideoCaptureHealthy);
        Assert.Null(beforeObservation.IsEncoderHealthy);

        orchestrator.ObserveRecorderHealth(new RecorderProgressObservation(
            true, 1, TimeSpan.FromMilliseconds(33), 1_024));

        var afterObservation = orchestrator.GetTelemetry();
        Assert.True(afterObservation.IsVideoCaptureHealthy);
        Assert.True(afterObservation.IsEncoderHealthy);
        await orchestrator.StopRecordingAsync();
    }

    [Fact]
    public async Task PausedRecording_TelemetryDoesNotReportFalseStall()
    {
        await using var orchestrator = CreateSyntheticOrchestrator();
        var (startSuccess, startError, _) = await orchestrator.StartRecordingAsync(
            CreateSyntheticConfiguration(deleteWorkingFiles: false));
        Assert.True(startSuccess, startError);
        var observation = new RecorderProgressObservation(
            true, 10, TimeSpan.FromSeconds(1), 4_096);
        orchestrator.ObserveRecorderHealth(observation);

        var (pauseSuccess, pauseError) = await orchestrator.PauseRecordingAsync();
        Assert.True(pauseSuccess, pauseError);
        orchestrator.ObserveRecorderHealth(observation);
        orchestrator.ObserveRecorderHealth(observation);
        orchestrator.ObserveRecorderHealth(observation);

        var telemetry = orchestrator.GetTelemetry();
        Assert.NotEqual(false, telemetry.IsVideoCaptureHealthy);
        Assert.NotEqual(false, telemetry.IsEncoderHealthy);
        Assert.Null(telemetry.HealthWarning);
        await orchestrator.StopRecordingAsync();
    }

    [Fact]
    public async Task HealthWarning_IsIncludedInTelemetryAfterConfirmedStall()
    {
        await using var orchestrator = CreateSyntheticOrchestrator();
        var (startSuccess, startError, _) = await orchestrator.StartRecordingAsync(
            CreateSyntheticConfiguration(deleteWorkingFiles: false));
        Assert.True(startSuccess, startError);
        var observation = new RecorderProgressObservation(
            true, 10, TimeSpan.FromSeconds(1), 4_096);

        orchestrator.ObserveRecorderHealth(observation);
        orchestrator.ObserveRecorderHealth(observation);
        orchestrator.ObserveRecorderHealth(observation);
        orchestrator.ObserveRecorderHealth(observation);

        var telemetry = orchestrator.GetTelemetry();
        Assert.False(telemetry.IsVideoCaptureHealthy);
        Assert.False(telemetry.IsEncoderHealthy);
        Assert.NotNull(telemetry.HealthWarning);
        await orchestrator.StopRecordingAsync();
    }

    [Fact]
    public async Task SessionHeartbeat_ContinuesWhileFinalizing()
    {
        var stateMachine = new RecordingStateMachine();
        var storageService = new StorageService();
        var sessionStore = new JsonRecordingSessionStore();
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var remuxer = new BlockingRemuxer();
        var probe = new MediaFileProbe();
        var displayService = new FixedDisplayService();

        await using var orchestrator = new RecordingOrchestrator(
            stateMachine,
            storageService,
            sessionStore,
            diskMonitor,
            remuxer,
            probe,
            displayService,
            new MacOsFFmpegProvider(displayService))
        {
            UseSyntheticCaptureSource = true
        };

        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            Region = new CaptureRegion(0, 0, 320, 240),
            Fps = 30,
            AudioSource = AudioSourceType.None,
            EncoderType = HardwareEncoderType.SoftwareCpu,
            OutputDirectory = _tempDir,
            DeleteWorkingFileAfterSuccessfulRemux = false
        };

        var (startSuccess, startError, _) = await orchestrator.StartRecordingAsync(config);
        Assert.True(startSuccess, startError);
        var sessionDirectory = Assert.IsType<string>(orchestrator.CurrentSession?.WorkingDirectory);
        await Task.Delay(500);

        var stopTask = orchestrator.StopRecordingAsync();
        await remuxer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var finalizing = await sessionStore.LoadSessionAsync(sessionDirectory);
        Assert.NotNull(finalizing);

        await Task.Delay(2500);
        var stillFinalizing = await sessionStore.LoadSessionAsync(sessionDirectory);
        Assert.NotNull(stillFinalizing);
        var heartbeatAdvanced = stillFinalizing.LastHeartbeatTime > finalizing.LastHeartbeatTime;

        remuxer.Release.TrySetResult();
        var (stopSuccess, stopError, _) = await stopTask;

        Assert.True(heartbeatAdvanced);
        Assert.True(stopSuccess, stopError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3000)]
    public async Task SessionHeartbeat_TransientStoreFailure_RetriesOnNextTick(int failureDelayMs)
    {
        var stateMachine = new RecordingStateMachine();
        var storageService = new StorageService();
        var sessionStore = new FailOnceRecordingSessionStore(failureDelayMs);
        stateMachine.StateChanged += (_, change) => sessionStore.Trace(
            $"state {change.PreviousState}->{change.NewState}: {change.Reason}");
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var displayService = new FixedDisplayService();

        await using var orchestrator = new RecordingOrchestrator(
            stateMachine,
            storageService,
            sessionStore,
            diskMonitor,
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            displayService,
            new MacOsFFmpegProvider(displayService))
        {
            UseSyntheticCaptureSource = true
        };

        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            Region = new CaptureRegion(0, 0, 320, 240),
            Fps = 30,
            AudioSource = AudioSourceType.None,
            EncoderType = HardwareEncoderType.SoftwareCpu,
            OutputDirectory = _tempDir,
            DeleteWorkingFileAfterSuccessfulRemux = false
        };

        var (startSuccess, startError, _) = await orchestrator.StartRecordingAsync(config);
        Assert.True(startSuccess, startError);
        var sessionDirectory = Assert.IsType<string>(orchestrator.CurrentSession?.WorkingDirectory);
        var started = await sessionStore.LoadSessionAsync(sessionDirectory);
        Assert.NotNull(started);

        sessionStore.FailNextSave();
        // Capture the Windows worker stacks when the first watchdog delay is late.
        // Keep the same fault, retry deadline and durable-save assertions.
        using var stackObserver = new DelayedHeartbeatStackObserver(() =>
            sessionStore.LastPersistedHeartbeatTicks <= started.LastHeartbeatTime.UtcTicks);
        // Wait for a completed durable retry, not two nominal timer intervals.
        // Stop must not supply the successful save that satisfies this test.
        try { await sessionStore.SavedAfterFailure.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (TimeoutException ex)
        {
            var watchdog = typeof(RecordingOrchestrator).GetField("_watchdogTask",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(orchestrator) as Task;
            throw new TimeoutException($"Heartbeat retry timed out; state={orchestrator.CurrentState}, watchdog={watchdog?.Status}, " +
                $"lastHeartbeat={orchestrator.CurrentSession?.LastHeartbeatTime:O}\n{string.Join("\n", sessionStore.Events)}\n" +
                $"Scheduling diagnostics before cleanup:\n{string.Join("\n", _diagnostics.Entries)}", ex);
        }
        Assert.Equal(RecordingState.Recording, orchestrator.CurrentState);

        Assert.Contains(_diagnostics.Entries, entry => entry.Contains("Watchdog schedule") && entry.Contains("delay-finished"));
        var afterRetry = await sessionStore.LoadSessionAsync(sessionDirectory);
        Assert.NotNull(afterRetry);
        var heartbeatAdvanced = afterRetry.LastHeartbeatTime > started.LastHeartbeatTime;

        var (stopSuccess, stopError, _) = await orchestrator.StopRecordingAsync();
        Assert.True(heartbeatAdvanced);
        Assert.True(stopSuccess, stopError);
    }

    [WindowsOnlyFact]
    public async Task SecondaryMonitorRecording_ShouldResolveBoundsAndRecord()
    {
        var stateMachine = new RecordingStateMachine();
        var storageService = new StorageService();
        var sessionStore = new JsonRecordingSessionStore();
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var remuxer = new StreamCopyRemuxer();
        var probe = new MediaFileProbe();
        var displayService = new WindowsDisplayService();

        await using var orchestrator = new RecordingOrchestrator(
            stateMachine,
            storageService,
            sessionStore,
            diskMonitor,
            remuxer,
            probe,
            displayService, new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider())
        {
            UseSyntheticCaptureSource = true
        };

        var monitors = displayService.GetMonitors();
        int targetIndex = monitors.Count > 1 ? 1 : 0;

        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.Monitor,
            MonitorIndex = targetIndex,
            Fps = 30,
            OutputDirectory = _tempDir,
            DeleteWorkingFileAfterSuccessfulRemux = false
        };

        var (startSuccess, startError, sessionId) = await orchestrator.StartRecordingAsync(config);
        Assert.True(startSuccess, $"指定螢幕錄影失敗: {startError}");
        Assert.NotNull(sessionId);

        await Task.Delay(2500);

        var (stopSuccess, stopError, finalMp4) = await orchestrator.StopRecordingAsync();
        Assert.True(stopSuccess, $"指定螢幕停止錄影或 Remux 失敗: {stopError}");
        Assert.NotNull(finalMp4);
        Assert.True(File.Exists(finalMp4));
    }

    [WindowsOnlyFact]
    public async Task HardwareAcceleratedRecording_ShouldRecordAndRemuxCleanly()
    {
        var stateMachine = new RecordingStateMachine();
        var storageService = new StorageService();
        var sessionStore = new JsonRecordingSessionStore();
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var remuxer = new StreamCopyRemuxer();
        var probe = new MediaFileProbe();
        var displayService = new WindowsDisplayService();

        await using var orchestrator = new RecordingOrchestrator(
            stateMachine,
            storageService,
            sessionStore,
            diskMonitor,
            remuxer,
            probe,
            displayService, new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider())
        {
            UseSyntheticCaptureSource = true
        };

        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            Region = new CaptureRegion(0, 0, 1280, 720),
            Fps = 30,
            EncoderType = HardwareEncoderType.Auto,
            OutputDirectory = _tempDir
        };

        var (startSuccess, startError, sessionId) = await orchestrator.StartRecordingAsync(config);
        Assert.True(startSuccess, $"指定螢幕錄影失敗: {startError}");

        await Task.Delay(2500);

        var (stopSuccess, stopError, finalMp4) = await orchestrator.StopRecordingAsync();
        Assert.True(stopSuccess, $"指定螢幕停止錄影或 Remux 失敗: {stopError}");
        Assert.NotNull(finalMp4);
        Assert.True(File.Exists(finalMp4));

        var probeResult = await probe.ProbeAsync(finalMp4!);
        Assert.True(probeResult.IsValid);
        Assert.Equal("h264", probeResult.VideoCodec);
        Assert.Equal(1280, probeResult.Width);
        Assert.Equal(720, probeResult.Height);
    }

    [WindowsOnlyFact]
    public async Task ConsecutiveRecordings_ShouldWorkTwice()
    {
        var stateMachine = new RecordingStateMachine();
        var storageService = new StorageService();
        var sessionStore = new JsonRecordingSessionStore();
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var remuxer = new StreamCopyRemuxer();
        var probe = new MediaFileProbe();
        var displayService = new WindowsDisplayService();

        await using var orchestrator = new RecordingOrchestrator(
            stateMachine,
            storageService,
            sessionStore,
            diskMonitor,
            remuxer,
            probe,
            displayService,
            new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider())
        {
            UseSyntheticCaptureSource = true
        };

        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            Region = new CaptureRegion(0, 0, 320, 240),
            Fps = 30,
            OutputDirectory = _tempDir,
            DeleteWorkingFileAfterSuccessfulRemux = false
        };

        // 第一次錄影
        var (s1, e1, _) = await orchestrator.StartRecordingAsync(config);
        Assert.True(s1, $"第一次錄影啟動失敗: {e1}");
        await Task.Delay(2000);
        var (stop1, stopErr1, mp4_1) = await orchestrator.StopRecordingAsync();
        Assert.True(stop1, $"第一次停止失敗: {stopErr1}");
        Assert.True(File.Exists(mp4_1));

        // 第二次錄影（模擬使用者再次點選開始錄影）
        var (s2, e2, _) = await orchestrator.StartRecordingAsync(config);
        Assert.True(s2, $"第二次錄影啟動失敗: {e2}");
        await Task.Delay(2000);
        var (stop2, stopErr2, mp4_2) = await orchestrator.StopRecordingAsync();
        Assert.True(stop2, $"第二次停止失敗: {stopErr2}");
        Assert.True(File.Exists(mp4_2));
        Assert.NotEqual(mp4_1, mp4_2);
    }

    [WindowsOnlyFact]
    public async Task ConsecutiveRecordings_WithSystemAudio_ShouldWorkTwice()
    {
        var stateMachine = new RecordingStateMachine();
        var storageService = new StorageService();
        var sessionStore = new JsonRecordingSessionStore();
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var remuxer = new StreamCopyRemuxer();
        var probe = new MediaFileProbe();
        var displayService = new WindowsDisplayService();
        var loopbackCapture = new WindowsWasapiLoopbackCapture();

        await using var orchestrator = new RecordingOrchestrator(
            stateMachine,
            storageService,
            sessionStore,
            diskMonitor,
            remuxer,
            probe,
            displayService,
            new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider(),
            systemAudioLoopbackCapture: loopbackCapture)
        {
            UseSyntheticCaptureSource = true
        };

        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            Region = new CaptureRegion(0, 0, 320, 240),
            Fps = 30,
            AudioSource = AudioSourceType.SystemOnly,
            OutputDirectory = _tempDir,
            DeleteWorkingFileAfterSuccessfulRemux = false
        };

        // 第一次系統音訊錄影
        var (s1, e1, _) = await orchestrator.StartRecordingAsync(config);
        Assert.True(s1, $"第一次系統聲音錄影啟動失敗: {e1}");
        await Task.Delay(2500);
        var (stop1, stopErr1, mp4_1) = await orchestrator.StopRecordingAsync();
        Assert.True(stop1, $"第一次系統聲音停止失敗: {stopErr1}");
        Assert.True(File.Exists(mp4_1));

        // 驗證第一次產出的 MP4 具有音訊軌且探針正確
        var probe1 = await probe.ProbeAsync(mp4_1!);
        Assert.True(probe1.IsValid);
        Assert.True(probe1.AudioStreamCount > 0, "應包含音訊軌");

        // 第二次系統音訊錄影
        var (s2, e2, _) = await orchestrator.StartRecordingAsync(config);
        Assert.True(s2, $"第二次系統聲音錄影啟動失敗: {e2}");
        await Task.Delay(2500);
        var (stop2, stopErr2, mp4_2) = await orchestrator.StopRecordingAsync();
        Assert.True(stop2, $"第二次系統聲音停止失敗: {stopErr2}");
        Assert.True(File.Exists(mp4_2));

        var probe2 = await probe.ProbeAsync(mp4_2!);
        Assert.True(probe2.IsValid);
        Assert.True(probe2.AudioStreamCount > 0, "第二次錄影亦應包含音訊軌");
    }

    [WindowsOnlyFact]
    public void WindowsAudioDeviceService_ShouldDetectMicrophonesWithValidEncodingAndAlternativeName()
    {
        var audioService = new WindowsAudioDeviceService();
        var devices = audioService.GetRecordingDevices();
        // 如果系統有麥克風，檢查名稱不可包含常見的 Big5 亂碼字元，且若有 alternative name 應正確填入 Id
        foreach (var dev in devices)
        {
            Assert.False(string.IsNullOrWhiteSpace(dev.Id));
            Assert.False(string.IsNullOrWhiteSpace(dev.Name));
            // 驗證不包含 Intel(R) 誤解碼成 Big5 的 '\u7C27'（簧）
            Assert.DoesNotContain("\u7C27", dev.Name);
            Assert.DoesNotContain("\u7C27", dev.Id);
        }
    }



    public void Dispose()
    {
        Serilog.Log.Logger = _previousLogger;
        _diagnosticLogger.Dispose();
        foreach (var entry in _diagnostics.Entries) _output.WriteLine(entry);
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    private sealed class RecordingDiagnosticSink : Serilog.Core.ILogEventSink
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Entries { get; } = new();
        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            var text = logEvent.RenderMessage() + (logEvent.Exception is null ? "" : "\n" + logEvent.Exception);
            Entries.Enqueue(text.Length > 12000 ? text[^12000..] : text);
            while (Entries.Count > 32) Entries.TryDequeue(out _);
        }
    }

    private RecordingConfiguration CreateSyntheticConfiguration(bool deleteWorkingFiles) =>
        new()
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            Region = new CaptureRegion(0, 0, 320, 240),
            Fps = 30,
            AudioSource = AudioSourceType.None,
            EncoderType = HardwareEncoderType.SoftwareCpu,
            OutputDirectory = _tempDir,
            DeleteWorkingFileAfterSuccessfulRemux = deleteWorkingFiles
        };

    private RecordingOrchestrator CreateSyntheticOrchestrator(RecordingCaptureLease? captureLease = null)
    {
        var storageService = new StorageService();
        var displayService = new FixedDisplayService();
        return new RecordingOrchestrator(
            new RecordingStateMachine(),
            storageService,
            new JsonRecordingSessionStore(),
            new DiskSpaceMonitor(storageService),
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            displayService,
            new MacOsFFmpegProvider(displayService), captureLease: captureLease)
        {
            UseSyntheticCaptureSource = true
        };
    }

    private sealed class FixedDisplayService : IDisplayService
    {
        private static readonly MonitorInfo Monitor = new(
            0,
            "Synthetic Display",
            new CaptureRegion(0, 0, 320, 240),
            true,
            1.0);

        public IReadOnlyList<MonitorInfo> GetMonitors() => new[] { Monitor };

        public MonitorInfo GetPrimaryMonitor() => Monitor;

        public CaptureRegion GetVirtualScreenBounds() => Monitor.Bounds;
    }

    private sealed class BlockingRemuxer : IStreamCopyRemuxer
    {
        private readonly StreamCopyRemuxer _inner = new();

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> RemuxToMp4Async(
            string mkvInputPath,
            string mp4OutputPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await _inner.RemuxToMp4Async(
                mkvInputPath,
                mp4OutputPath,
                progress,
                cancellationToken);
        }

        public async Task<bool> ConcatAndRemuxToMp4Async(
            IReadOnlyList<string> mkvInputPaths,
            string mp4OutputPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await _inner.ConcatAndRemuxToMp4Async(
                mkvInputPaths,
                mp4OutputPath,
                progress,
                cancellationToken);
        }
    }

    private sealed class InvalidMediaProbe : IMediaProbeService
    {
        public Task<MediaProbeResult> ProbeAsync(
            string mediaFilePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaProbeResult(
                false,
                string.Empty,
                TimeSpan.Zero,
                0,
                0,
                0,
                null,
                0,
                0,
                0,
                null,
                0,
                null));
    }

    private sealed class FailingRemuxer : IStreamCopyRemuxer
    {
        public Task<bool> RemuxToMp4Async(
            string mkvInputPath,
            string mp4OutputPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ConcatAndRemuxToMp4Async(
            IReadOnlyList<string> mkvInputPaths,
            string mp4OutputPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FailOnceRecordingSessionStore(int failureDelayMs = 0) : IRecordingSessionStore
    {
        private readonly JsonRecordingSessionStore _inner = new();
        private int _failNextSave;
        private int _failureInjected;
        private long _lastPersistedHeartbeatTicks;
        private TaskCompletionSource _saveCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long LastPersistedHeartbeatTicks => Interlocked.Read(ref _lastPersistedHeartbeatTicks);
        public async Task WaitForHeartbeatAfterAsync(DateTimeOffset previous, CancellationToken cancellationToken)
        {
            while (LastPersistedHeartbeatTicks <= previous.UtcTicks)
            {
                var completion = Volatile.Read(ref _saveCompleted);
                // Recheck after subscribing so a save between the first check and subscription is not lost.
                if (LastPersistedHeartbeatTicks > previous.UtcTicks) return;
                await completion.Task.WaitAsync(cancellationToken);
            }
        }
        public TaskCompletionSource SavedAfterFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public System.Collections.Concurrent.ConcurrentQueue<string> Events { get; } = new();
        public void Trace(string message)
        {
            Events.Enqueue($"{DateTimeOffset.UtcNow:O} {message}");
            while (Events.Count > 40) Events.TryDequeue(out _);
        }

        public void FailNextSave() => Interlocked.Exchange(ref _failNextSave, 1);

        public async Task SaveSessionAsync(
            RecordingSession session,
            CancellationToken cancellationToken = default)
        {
            Trace($"save entered; failureInjected={Volatile.Read(ref _failureInjected)}");
            if (Interlocked.Exchange(ref _failNextSave, 0) == 1)
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                var delay = Task.Delay(failureDelayMs, cancellationToken);
                Trace($"fault-delay created; requestedMs={failureDelayMs}, " +
                    $"context={SynchronizationContext.Current?.GetType().Name ?? "none"}, scheduler={TaskScheduler.Current.GetType().Name}");
                using var delayProbe = new IndependentDelayProbe(delay, Trace);
                await delay;
                Trace($"fault-delay await resumed; elapsedMs={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}, " +
                    $"status={delay.Status}");
                Interlocked.Exchange(ref _failureInjected, 1);
                Trace("injecting save failure");
                throw new IOException("Injected transient session-store failure");
            }

            var isRetry = Volatile.Read(ref _failureInjected) == 1;
            var heartbeatTicks = session.LastHeartbeatTime.UtcTicks;
            await _inner.SaveSessionAsync(session, cancellationToken);
            Interlocked.Exchange(ref _lastPersistedHeartbeatTicks, heartbeatTicks);
            Interlocked.Exchange(ref _saveCompleted, new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
            Trace($"save persisted; isRetry={isRetry}");
            if (isRetry) SavedAfterFailure.TrySetResult();
        }

        public async Task<RecordingSession?> LoadSessionAsync(
            string sessionDirectory,
            CancellationToken cancellationToken = default)
        {
            Trace("load entered");
            var session = await _inner.LoadSessionAsync(sessionDirectory, cancellationToken);
            Trace($"load completed; heartbeat={session?.LastHeartbeatTime:O}");
            return session;
        }

        public Task<IReadOnlyList<RecordingSession>> FindAllSessionsAsync(
            string rootRecordingsPath,
            CancellationToken cancellationToken = default) =>
            _inner.FindAllSessionsAsync(rootRecordingsPath, cancellationToken);

        public Task DeleteSessionAsync(
            string sessionDirectory,
            CancellationToken cancellationToken = default) =>
            _inner.DeleteSessionAsync(sessionDirectory, cancellationToken);
    }
}
