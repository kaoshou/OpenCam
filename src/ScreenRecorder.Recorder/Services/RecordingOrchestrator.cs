using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.State;
using ScreenRecorder.Media.Capture;
using Serilog;

namespace ScreenRecorder.Recorder.Services;

public class RecordingOrchestrator : IAsyncDisposable
{
    private readonly IRecordingStateMachine _stateMachine;
    private readonly IStorageService _storageService;
    private readonly IRecordingSessionStore _sessionStore;
    private readonly IDiskSpaceMonitor _diskMonitor;
    private readonly IStreamCopyRemuxer _remuxer;
    private readonly IMediaProbeService _mediaProbe;
    private readonly IDisplayService _displayService;
    private readonly IFFmpegPlatformProvider _ffmpegPlatformProvider;
    private readonly ISystemAudioLoopbackCapture? _systemAudioLoopbackCapture;
    private readonly IMicrophoneCapture? _microphoneCapture;
    private readonly IDisplayChangeMonitor? _displayChangeMonitor;
    private readonly ICursorHighlightService? _cursorHighlightService;

    private IScreenRecorderEngine? _engine;
    private RecordingSession? _currentSession;
    private CancellationTokenSource? _watchdogCts;
    private Task? _watchdogTask;
    private TimeSpan _accumulatedDuration = TimeSpan.Zero;
    private int _segmentIndex = 0;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _sessionStoreGate = new(1, 1);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    public bool UseSyntheticCaptureSource { get; set; }

    public RecordingState CurrentState => _stateMachine.CurrentState;
    public RecordingSession? CurrentSession => _currentSession;

    public event EventHandler<string>? WatchdogWarningOccurred;
    public event EventHandler<string>? EmergencyStopTriggered;

    public RecordingOrchestrator(
        IRecordingStateMachine stateMachine,
        IStorageService storageService,
        IRecordingSessionStore sessionStore,
        IDiskSpaceMonitor diskMonitor,
        IStreamCopyRemuxer remuxer,
        IMediaProbeService mediaProbe,
        IDisplayService displayService,
        IFFmpegPlatformProvider ffmpegPlatformProvider,
        ISystemAudioLoopbackCapture? systemAudioLoopbackCapture = null,
        IDisplayChangeMonitor? displayChangeMonitor = null,
        ICursorHighlightService? cursorHighlightService = null,
        IMicrophoneCapture? microphoneCapture = null)
    {
        _stateMachine = stateMachine;
        _storageService = storageService;
        _sessionStore = sessionStore;
        _diskMonitor = diskMonitor;
        _remuxer = remuxer;
        _mediaProbe = mediaProbe;
        _displayService = displayService;
        _ffmpegPlatformProvider = ffmpegPlatformProvider;
        _systemAudioLoopbackCapture = systemAudioLoopbackCapture;
        _microphoneCapture = microphoneCapture;
        _displayChangeMonitor = displayChangeMonitor;
        _cursorHighlightService = cursorHighlightService;

        _diskMonitor.DiskSpaceCriticalTriggered += OnDiskSpaceCritical;

        if (_displayChangeMonitor != null)
        {
            _displayChangeMonitor.DisplayChanged += OnDisplayChanged;
            _displayChangeMonitor.Start();
        }
    }

    public async Task<(bool Success, string? ErrorMessage, string? SessionId)> StartRecordingAsync(
        RecordingConfiguration config, 
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            return await StartRecordingCoreAsync(config, cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<(bool Success, string? ErrorMessage, string? SessionId)> StartRecordingCoreAsync(
        RecordingConfiguration config,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_stateMachine.CurrentState == RecordingState.Completed || _stateMachine.CurrentState == RecordingState.Failed)
            {
                _stateMachine.Reset();
            }

            if (!_stateMachine.CanTransitionTo(RecordingState.Preparing))
            {
                return (false, $"目前狀態 {_stateMachine.CurrentState} 無法啟動錄影", null);
            }
        }

        var rootPath = string.IsNullOrWhiteSpace(config.OutputDirectory) 
            ? _storageService.GetDefaultRecordingsPath() 
            : config.OutputDirectory;

        // 磁碟空間檢查 (至少 1 GB)
        if (!_storageService.IsDiskSpaceSufficient(rootPath, 1L * 1024 * 1024 * 1024))
        {
            return (false, "目標磁碟空間不足 (至少需要 1 GB)", null);
        }

        _stateMachine.TryTransition(RecordingState.Preparing, "開始初始化錄影管線");

        try
        {
            // Paused sessions are split into multiple files and later joined
            // with stream copy. Keep an AAC stereo track in every segment so
            // enabling or disabling audio while paused never changes the
            // stream layout and silently drops later audio during concat.
            config.MaintainSegmentAudioTrack = true;

            // 計算實際擷取幾何範圍
            var actualBounds = ResolveCaptureBounds(config);

            var sessionId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6];
            var sessionDir = _storageService.CreateSessionDirectory(rootPath, sessionId);
            _accumulatedDuration = TimeSpan.Zero;
            _segmentIndex = 0;
            var workingMvk = Path.Combine(sessionDir, $"segment_{_segmentIndex:D3}.mkv");
            var finalMp4 = _storageService.GetFinalFilePath(rootPath, DateTimeOffset.Now);
            var sessionLog = _storageService.GetSessionLogFilePath(sessionDir);

            var session = new RecordingSession
            {
                SessionId = sessionId,
                StartTime = DateTimeOffset.Now,
                LastHeartbeatTime = DateTimeOffset.Now,
                State = RecordingState.Preparing,
                Configuration = config,
                OutputWidth = actualBounds.Width,
                OutputHeight = actualBounds.Height,
                WorkingDirectory = sessionDir,
                WorkingFilePath = workingMvk,
                SegmentFilePaths = new List<string> { workingMvk },
                FinalFilePath = finalMp4,
                LogFilePath = sessionLog
            };

            await SaveSessionAsync(session, cancellationToken);
            _currentSession = session;

            _engine = new FFmpegScreenRecorderEngine(
                _ffmpegPlatformProvider,
                _systemAudioLoopbackCapture,
                microphoneCapture: _microphoneCapture)
            {
                UseSyntheticCaptureSource = UseSyntheticCaptureSource
            };
            _engine.EngineErrorOccurred += (s, err) =>
            {
                Log.Error("錄影引擎報告錯誤: {Error}", err);
                _stateMachine.ForceTransition(RecordingState.Failed, err);
                if (_currentSession != null)
                {
                    _currentSession.State = RecordingState.Failed;
                    _currentSession.ErrorMessage = err;
                    try { SaveSessionAsync(_currentSession).GetAwaiter().GetResult(); } catch { }
                }
            };
            _engine.EngineWarningOccurred += (s, warn) =>
            {
                Log.Warning("錄影引擎發出警告: {Warning}", warn);
            };
            _engine.AudioDeviceLost += OnAudioDeviceLost;

            await _engine.StartRecordingAsync(workingMvk, config, actualBounds, cancellationToken);

            _stateMachine.TryTransition(RecordingState.Recording, "錄影引擎已啟動");
            session.State = RecordingState.Recording;
            await SaveSessionAsync(session, cancellationToken);

            ApplyDiskGuardThresholds(config);
            _diskMonitor.StartMonitoring(rootPath, TimeSpan.FromSeconds(2));

            _watchdogCts?.Cancel();
            _watchdogCts?.Dispose();
            _watchdogCts = new CancellationTokenSource();
            _watchdogTask = Task.Run(() => WatchdogLoopAsync(session, _watchdogCts.Token));

            _cursorHighlightService?.Start(config.CursorEffect);

            Log.Information("錄影工作階段啟動成功: {SessionId}, 解析度: {W}x{H}, 輸出檔: {Output}", 
                sessionId, actualBounds.Width, actualBounds.Height, finalMp4);

            return (true, null, sessionId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "啟動錄影失敗");
            _stateMachine.ForceTransition(RecordingState.Failed, ex.Message);
            if (_currentSession != null)
            {
                _currentSession.State = RecordingState.Failed;
                _currentSession.ErrorMessage = ex.Message;
                try { await SaveSessionAsync(_currentSession, cancellationToken); } catch { }
            }
            return (false, ex.Message, null);
        }
    }

    private void ApplyDiskGuardThresholds(RecordingConfiguration config)
    {
        var warningThreshold = config.DiskWarningThresholdBytes;
        var criticalThreshold = config.DiskCriticalThresholdBytes;

        if (warningThreshold <= 0 ||
            criticalThreshold <= 0 ||
            warningThreshold <= criticalThreshold)
        {
            warningThreshold = RecordingConfiguration.DefaultDiskWarningThresholdBytes;
            criticalThreshold = RecordingConfiguration.DefaultDiskCriticalThresholdBytes;
        }

        _diskMonitor.WarningThresholdBytes = warningThreshold;
        _diskMonitor.CriticalThresholdBytes = criticalThreshold;
    }

    public async Task<(bool Success, string? ErrorMessage)> UpdatePausedConfigurationAsync(
        RecordingConfiguration updates,
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            return await UpdatePausedConfigurationCoreAsync(updates, cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<(bool Success, string? ErrorMessage)> UpdatePausedConfigurationCoreAsync(
        RecordingConfiguration updates,
        CancellationToken cancellationToken)
    {
        RecordingSession session;
        lock (_lock)
        {
            if (_currentSession == null ||
                _stateMachine.CurrentState != RecordingState.Paused)
            {
                return (false, "只有在錄影暫停時才能更新音訊與游標設定");
            }

            _currentSession.Configuration.ApplyPausedSettings(updates);
            session = _currentSession;
        }

        try
        {
            await SaveSessionAsync(session, cancellationToken);
            Log.Information(
                "使用者更新暫停期間設定: AudioSource={AudioSource}, Mic={Mic}, Cursor={Cursor}",
                session.Configuration.AudioSource,
                session.Configuration.MicrophoneDeviceId,
                session.Configuration.CursorEffect);
            return (true, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "儲存暫停期間設定失敗");
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> PauseRecordingAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            return await PauseRecordingCoreAsync(cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<(bool Success, string? ErrorMessage)> PauseRecordingCoreAsync(
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_currentSession == null || !_stateMachine.CanTransitionTo(RecordingState.Pausing))
            {
                return (false, $"目前狀態 {_stateMachine.CurrentState} 無法暫停錄影");
            }

            _stateMachine.TryTransition(RecordingState.Pausing, "使用者要求暫停錄影");
        }

        _cursorHighlightService?.Stop();

        try
        {
            if (_engine != null)
            {
                await _engine.StopRecordingAsync(cancellationToken);
                _accumulatedDuration += _engine.CurrentRecordedTime;
                if (_currentSession != null)
                {
                    _currentSession.TotalVideoFramesRecorded += _engine.CurrentFramesRecorded;
                }
                _engine = null;
            }

            lock (_lock)
            {
                _stateMachine.TryTransition(RecordingState.Paused, "錄影已安全暫停");
                if (_currentSession != null)
                {
                    _currentSession.State = RecordingState.Paused;
                }
            }

            if (_currentSession != null)
            {
                await SaveSessionAsync(_currentSession, cancellationToken);
                Log.Information("錄影已成功暫停，累計時長: {Elapsed}, 分段數: {Count}", 
                    _accumulatedDuration, _currentSession.SegmentFilePaths.Count);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "暫停錄影處置發生錯誤");
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> ResumeRecordingAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            return await ResumeRecordingCoreAsync(cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<(bool Success, string? ErrorMessage)> ResumeRecordingCoreAsync(
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_currentSession == null || !_stateMachine.CanTransitionTo(RecordingState.Recording))
            {
                return (false, $"目前狀態 {_stateMachine.CurrentState} 無法繼續錄影");
            }
        }

        try
        {
            var session = _currentSession!;
            _segmentIndex++;
            var nextSegment = Path.Combine(session.WorkingDirectory, $"segment_{_segmentIndex:D3}.mkv");
            session.SegmentFilePaths.Add(nextSegment);
            session.WorkingFilePath = nextSegment;

            var actualBounds = ResolveCaptureBounds(session.Configuration);

            _engine = new FFmpegScreenRecorderEngine(
                _ffmpegPlatformProvider,
                _systemAudioLoopbackCapture,
                microphoneCapture: _microphoneCapture)
            {
                UseSyntheticCaptureSource = UseSyntheticCaptureSource
            };
            _engine.EngineErrorOccurred += (s, err) =>
            {
                Log.Error("錄影引擎報告錯誤: {Error}", err);
                _stateMachine.ForceTransition(RecordingState.Failed, err);
            };
            _engine.EngineWarningOccurred += (s, warn) => Log.Warning("錄影引擎發出警告: {Warning}", warn);
            _engine.AudioDeviceLost += OnAudioDeviceLost;

            await _engine.StartRecordingAsync(nextSegment, session.Configuration, actualBounds, cancellationToken);

            lock (_lock)
            {
                _stateMachine.TryTransition(RecordingState.Recording, "繼續錄影成功");
                session.State = RecordingState.Recording;
            }

            await SaveSessionAsync(session, cancellationToken);

            if (_watchdogTask == null || _watchdogTask.IsCompleted)
            {
                _watchdogCts?.Cancel();
                _watchdogCts?.Dispose();
                _watchdogCts = new CancellationTokenSource();
                _watchdogTask = Task.Run(() => WatchdogLoopAsync(session, _watchdogCts.Token));
            }

            _cursorHighlightService?.Start(session.Configuration.CursorEffect);

            Log.Information("錄影已繼續，開始錄製分段 {Index}: {Path}", _segmentIndex, nextSegment);
            return (true, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "繼續錄影失敗");
            _stateMachine.ForceTransition(RecordingState.Failed, ex.Message);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? ErrorMessage, string? FinalFilePath)> StopRecordingAsync(
        string reason = "User requested stop", 
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            return await StopRecordingCoreAsync(reason, cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<(bool Success, string? ErrorMessage, string? FinalFilePath)> StopRecordingCoreAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_currentSession == null ||
                !_stateMachine.TryTransition(RecordingState.Stopping, reason))
            {
                return (false, $"目前狀態 {_stateMachine.CurrentState} 無法停止錄影", null);
            }

        }
        _diskMonitor.StopMonitoring();

        _cursorHighlightService?.Stop();

        var session = _currentSession;
        session.EndTime = DateTimeOffset.Now;
        session.StopReason = reason;

        try
        {
            session.State = RecordingState.Stopping;
            await SaveSessionAsync(session, cancellationToken);

            // 1. 安全關閉 MKV 錄影引擎 (若在 Paused 狀態下停止，_engine 已為 null)
            if (_engine != null)
            {
                await _engine.StopRecordingAsync(cancellationToken);
                _accumulatedDuration += _engine.CurrentRecordedTime;
                session.TotalVideoFramesRecorded += _engine.CurrentFramesRecorded;
                _engine = null;
            }

            // 2. 切換為 Finalizing
            _stateMachine.TryTransition(RecordingState.Finalizing, "開始 Remux MKV -> MP4");
            session.State = RecordingState.Finalizing;
            await SaveSessionAsync(session, cancellationToken);

            // 3. 無損轉碼 Stream Copy Remux (支援單段或多段無損 Concat 拼接)
            bool remuxSuccess;
            if (session.SegmentFilePaths.Count > 1)
            {
                Log.Information("正在執行多段 MKV -> MP4 無損 Concat 拼接: 共 {Count} 段 -> {Mp4}", session.SegmentFilePaths.Count, session.FinalFilePath);
                remuxSuccess = await _remuxer.ConcatAndRemuxToMp4Async(session.SegmentFilePaths, session.FinalFilePath, null, cancellationToken);
            }
            else
            {
                var inputMkv = session.SegmentFilePaths.FirstOrDefault() ?? session.WorkingFilePath;
                if (!File.Exists(inputMkv))
                {
                    throw new FileNotFoundException($"工作檔 MKV 不存在: {inputMkv}");
                }
                Log.Information("正在執行單段 MKV -> MP4 無損封裝轉換: {Mkv} -> {Mp4}", inputMkv, session.FinalFilePath);
                remuxSuccess = await _remuxer.RemuxToMp4Async(inputMkv, session.FinalFilePath, null, cancellationToken);
            }

            if (!remuxSuccess || !File.Exists(session.FinalFilePath))
            {
                throw new InvalidOperationException("Remux 轉換失敗或未產生 MP4 成品");
            }

            // 4. ffprobe 探針驗證
            var probeResult = await _mediaProbe.ProbeAsync(session.FinalFilePath, cancellationToken);
            if (!probeResult.IsValid || probeResult.VideoStreamCount < 1)
            {
                throw new InvalidOperationException("生成的 MP4 檔案驗證無效 (無有效視訊流)");
            }

            Log.Information("MP4 檔案驗證成功: 時長 {Duration}, 解析度 {W}x{H}, 大小 {Bytes} bytes",
                probeResult.Duration, probeResult.Width, probeResult.Height, probeResult.FileSizeBytes);

            session.FileSizeBytes = probeResult.FileSizeBytes;

            // 5. 依設定處理工作檔 (預設保留原始 MKV 以防萬一)
            if (session.Configuration.DeleteWorkingFileAfterSuccessfulRemux)
            {
                foreach (var seg in session.SegmentFilePaths)
                {
                    try { if (File.Exists(seg)) File.Delete(seg); } catch { }
                }
            }

            _stateMachine.TryTransition(RecordingState.Completed, "錄影與驗收全數完成");
            session.State = RecordingState.Completed;
            await SaveSessionAsync(session, cancellationToken);

            var finalPath = session.FinalFilePath;

            // 完全重置錄影工作階段狀態，確保下一次錄影乾淨無殘留
            _currentSession = null;
            _accumulatedDuration = TimeSpan.Zero;
            _segmentIndex = 0;

            return (true, null, finalPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "停止錄影或 Remux 過程發生錯誤");
            _stateMachine.ForceTransition(RecordingState.Failed, ex.Message);
            session.State = RecordingState.Failed;
            session.ErrorMessage = ex.Message;
            try { await SaveSessionAsync(session, cancellationToken); } catch { }

            // 確保原始 MKV 被保留
            return (false, ex.Message, null);
        }
        finally
        {
            await StopWatchdogAsync();
        }
    }

    public RecorderTelemetry GetTelemetry()
    {
        var session = _currentSession;
        if (session == null)
        {
            return new RecorderTelemetry { State = _stateMachine.CurrentState };
        }

        var currentSegmentElapsed = _engine?.CurrentRecordedTime ?? TimeSpan.Zero;
        var elapsed = _stateMachine.CurrentState == RecordingState.Paused 
            ? _accumulatedDuration 
            : _accumulatedDuration + currentSegmentElapsed;

        long totalFileSize = 0;
        var segments = session.SegmentFilePaths.Count > 0 ? session.SegmentFilePaths : new List<string> { session.WorkingFilePath };
        foreach (var seg in segments)
        {
            if (File.Exists(seg))
            {
                try { totalFileSize += new FileInfo(seg).Length; } catch { }
            }
        }

        var freeSpace = _storageService.GetAvailableFreeSpaceBytes(session.WorkingDirectory);

        return new RecorderTelemetry
        {
            SessionId = session.SessionId,
            State = _stateMachine.CurrentState,
            ElapsedTime = elapsed,
            WorkingFilePath = session.WorkingFilePath,
            FinalFilePath = session.FinalFilePath,
            CurrentFileSizeBytes = totalFileSize,
            DroppedFrames = 0,
            AvailableDiskSpaceBytes = freeSpace,
            IsVideoCaptureHealthy = true,
            IsSystemAudioHealthy = true,
            IsMicrophoneHealthy = true,
            IsEncoderHealthy = true
        };
    }

    private CaptureRegion ResolveCaptureBounds(RecordingConfiguration config)
    {
        var monitors = _displayService.GetMonitors();

        if (config.CaptureSource == CaptureSourceType.CustomRegion)
        {
            var r = config.Region;
            return new CaptureRegion(r.X, r.Y, r.Width, r.Height);
        }

        if (config.CaptureSource == CaptureSourceType.Monitor && 
            config.MonitorIndex >= 0 && 
            config.MonitorIndex < monitors.Count)
        {
            return monitors[config.MonitorIndex].Bounds;
        }

        // 全螢幕或預設主螢幕
        var primary = _displayService.GetPrimaryMonitor() ?? monitors.FirstOrDefault();
        return primary?.Bounds ?? new CaptureRegion(0, 0, 1920, 1080);
    }

    private int _audioRecoveryInFlight;

    private async void OnAudioDeviceLost(object? sender, EventArgs e)
    {
        if (Interlocked.CompareExchange(ref _audioRecoveryInFlight, 1, 0) != 0)
        {
            return;
        }

        var requiresEmergencyStop = false;
        await _lifecycleGate.WaitAsync();

        try
        {
            if (_currentSession == null ||
                (sender != null && !ReferenceEquals(sender, _engine)) ||
                _stateMachine.CurrentState != RecordingState.Recording)
            {
                return;
            }

            Log.Warning("偵測到音訊裝置拔除或異常崩潰，準備觸發安全靜音補償接續錄製...");

            var session = _currentSession;
            session.Configuration.IsRecoverySilenceMode = true;

            if (_engine != null)
            {
                await _engine.StopRecordingAsync(default);
                _accumulatedDuration += _engine.CurrentRecordedTime;
                session.TotalVideoFramesRecorded += _engine.CurrentFramesRecorded;
            }

            _segmentIndex++;
            var nextSegment = Path.Combine(session.WorkingDirectory, $"segment_{_segmentIndex:D3}.mkv");
            session.SegmentFilePaths.Add(nextSegment);
            session.WorkingFilePath = nextSegment;

            var actualBounds = ResolveCaptureBounds(session.Configuration);

            _engine = new FFmpegScreenRecorderEngine(
                _ffmpegPlatformProvider,
                _systemAudioLoopbackCapture,
                microphoneCapture: _microphoneCapture)
            {
                UseSyntheticCaptureSource = UseSyntheticCaptureSource
            };
            _engine.EngineErrorOccurred += (s, err) =>
            {
                Log.Error("錄影引擎報告錯誤: {Error}", err);
                _stateMachine.ForceTransition(RecordingState.Failed, err);
            };
            _engine.EngineWarningOccurred += (s, warn) => Log.Warning("錄影引擎發出警告: {Warning}", warn);
            _engine.AudioDeviceLost += OnAudioDeviceLost;

            await _engine.StartRecordingAsync(nextSegment, session.Configuration, actualBounds, default);
            
            await SaveSessionAsync(session, default);
            
            Log.Information("已成功切換至靜音補償模式，繼續錄製分段 {Index}: {Path}", _segmentIndex, nextSegment);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "音訊裝置拔除自動恢復發生錯誤");
            EmergencyStopTriggered?.Invoke(this, $"音訊裝置拔除後無法自動恢復錄影: {ex.Message}");
            requiresEmergencyStop = true;
        }
        finally
        {
            _lifecycleGate.Release();
            Interlocked.Exchange(ref _audioRecoveryInFlight, 0);
        }

        if (requiresEmergencyStop)
        {
            await StopRecordingAsync("音訊裝置異常觸發安全停機");
        }
    }

    private async void OnDiskSpaceCritical(object? sender, long remainingBytes)
    {
        Log.Warning("磁碟空間觸及臨界門檻 ({Bytes} bytes)，啟動主動安全停止", remainingBytes);
        try
        {
            await StopRecordingAsync("磁碟剩餘空間不足觸發安全停機");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "磁碟不足安全停機處置發生錯誤");
        }
    }

    private async Task WatchdogLoopAsync(RecordingSession session, CancellationToken ct)
    {
        long lastSize = -1;
        int stallCount = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);

                var currentState = _stateMachine.CurrentState;
                if (currentState is not RecordingState.Recording and
                    not RecordingState.Pausing and
                    not RecordingState.Paused and
                    not RecordingState.Stopping and
                    not RecordingState.Finalizing)
                {
                    break;
                }

                session.LastHeartbeatTime = DateTimeOffset.UtcNow;
                try
                {
                    await SaveSessionAsync(session, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        ex,
                        "寫入錄影工作階段心跳失敗，下一輪將自動重試: {SessionId}",
                        session.SessionId);
                }

                if (currentState != RecordingState.Recording)
                {
                    stallCount = 0;
                    continue;
                }

                if (File.Exists(session.WorkingFilePath))
                {
                    long currentSize = 0;
                    try { currentSize = new FileInfo(session.WorkingFilePath).Length; } catch { }

                    if (currentSize == lastSize && currentSize > 0)
                    {
                        stallCount++;
                        if (stallCount >= 3)
                        {
                            Log.Warning("看門狗監測：錄影檔案大小已逾 6 秒無增長 ({Bytes} bytes)", currentSize);
                            WatchdogWarningOccurred?.Invoke(this, "錄影管線寫入出現遲滯，看門狗正全力維護錄影資料安全");
                        }
                    }
                    else
                    {
                        stallCount = 0;
                    }
                    lastSize = currentSize;
                }

                var ws = Environment.WorkingSet;
                if (ws > 2L * 1024 * 1024 * 1024)
                {
                    Log.Warning("看門狗監測：主程序記憶體用量偏高: {MB} MB", ws / (1024 * 1024));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "看門狗背景監測發生非致命例外");
        }
    }

    private async Task SaveSessionAsync(
        RecordingSession session,
        CancellationToken cancellationToken = default)
    {
        await _sessionStoreGate.WaitAsync(cancellationToken);
        try
        {
            await _sessionStore.SaveSessionAsync(session, cancellationToken);
        }
        finally
        {
            _sessionStoreGate.Release();
        }
    }

    private async Task StopWatchdogAsync()
    {
        var cancellation = _watchdogCts;
        var task = _watchdogTask;

        cancellation?.Cancel();
        if (task != null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
        if (ReferenceEquals(_watchdogCts, cancellation))
        {
            _watchdogCts = null;
        }
        if (ReferenceEquals(_watchdogTask, task))
        {
            _watchdogTask = null;
        }
    }

    private async void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (_stateMachine.CurrentState != RecordingState.Recording || _currentSession == null)
        {
            return;
        }

        Log.Warning("監測到 Windows 顯示設定或螢幕熱插拔變更，正在評估錄影邊界安全性");

        try
        {
            var monitors = _displayService.GetMonitors();
            var cfg = _currentSession.Configuration;

            bool isInvalid = false;
            if (cfg.CaptureSource == CaptureSourceType.Monitor)
            {
                if (cfg.MonitorIndex < 0 || cfg.MonitorIndex >= monitors.Count)
                {
                    isInvalid = true;
                }
            }
            else if (cfg.CaptureSource == CaptureSourceType.CustomRegion)
            {
                var virtualBounds = _displayService.GetVirtualScreenBounds();
                if (cfg.Region.X + cfg.Region.Width > virtualBounds.X + virtualBounds.Width ||
                    cfg.Region.Y + cfg.Region.Height > virtualBounds.Y + virtualBounds.Height)
                {
                    isInvalid = true;
                }
            }

            if (isInvalid)
            {
                Log.Warning("錄影目標螢幕或自訂選區已因顯示變更超出合法範圍，立即執行緊急安全停止");
                EmergencyStopTriggered?.Invoke(this, "偵測到螢幕解析度或顯示設定變更，已安全收斂停止並保存當前錄影。");
                await StopRecordingAsync("螢幕解析度變更觸發安全停機");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "螢幕熱插拔安全處置發生錯誤");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            _diskMonitor.DiskSpaceCriticalTriggered -= OnDiskSpaceCritical;
            if (_displayChangeMonitor != null)
            {
                _displayChangeMonitor.DisplayChanged -= OnDisplayChanged;
                _displayChangeMonitor.Stop();
            }

            if (_stateMachine.CurrentState is
                RecordingState.Recording or
                RecordingState.Pausing or
                RecordingState.Paused)
            {
                await StopRecordingCoreAsync("Orchestrator 處置退出", default);
            }

            if (_engine != null)
            {
                await _engine.DisposeAsync();
                _engine = null;
            }

            await StopWatchdogAsync();
            _diskMonitor.Dispose();
            _sessionStoreGate.Dispose();
            GC.SuppressFinalize(this);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }
}
