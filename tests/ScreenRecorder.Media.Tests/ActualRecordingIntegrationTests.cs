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

    public ActualRecordingIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RealRecordingTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
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
        var sessionStore = new JsonRecordingSessionStore();
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

        await Task.Delay(2500);
        var whileRecording = await sessionStore.LoadSessionAsync(sessionDirectory);
        Assert.NotNull(whileRecording);
        Assert.True(whileRecording.LastHeartbeatTime > started.LastHeartbeatTime);

        var (pauseSuccess, pauseError) = await orchestrator.PauseRecordingAsync();
        Assert.True(pauseSuccess, pauseError);
        var paused = await sessionStore.LoadSessionAsync(sessionDirectory);
        Assert.NotNull(paused);

        await Task.Delay(2500);
        var whilePaused = await sessionStore.LoadSessionAsync(sessionDirectory);
        Assert.NotNull(whilePaused);
        Assert.True(whilePaused.LastHeartbeatTime > paused.LastHeartbeatTime);

        var (stopSuccess, stopError, _) = await orchestrator.StopRecordingAsync();
        Assert.True(stopSuccess, stopError);
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

    [Fact]
    public async Task SessionHeartbeat_TransientStoreFailure_RetriesOnNextTick()
    {
        var stateMachine = new RecordingStateMachine();
        var storageService = new StorageService();
        var sessionStore = new FailOnceRecordingSessionStore();
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
        await Task.Delay(4500);

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
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
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

    private sealed class FailOnceRecordingSessionStore : IRecordingSessionStore
    {
        private readonly JsonRecordingSessionStore _inner = new();
        private int _failNextSave;

        public void FailNextSave() => Interlocked.Exchange(ref _failNextSave, 1);

        public Task SaveSessionAsync(
            RecordingSession session,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _failNextSave, 0) == 1)
            {
                throw new IOException("Injected transient session-store failure");
            }

            return _inner.SaveSessionAsync(session, cancellationToken);
        }

        public Task<RecordingSession?> LoadSessionAsync(
            string sessionDirectory,
            CancellationToken cancellationToken = default) =>
            _inner.LoadSessionAsync(sessionDirectory, cancellationToken);

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
