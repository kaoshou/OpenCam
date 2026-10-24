// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Encoders;
using Serilog;

namespace ScreenRecorder.Media.Capture;

public interface IScreenRecorderEngine : IAsyncDisposable
{
    bool IsRunning { get; }
    bool UseSyntheticCaptureSource { get; set; }
    TimeSpan CurrentRecordedTime { get; }
    long CurrentFramesRecorded { get; }
    HardwareEncoderType ActiveEncoder { get; }
    CaptureSelection? ActiveCapture { get; }
    Task StartRecordingAsync(string workingFilePath, RecordingConfiguration config, CaptureRegion actualBounds, CancellationToken cancellationToken = default);
    Task StopRecordingAsync(CancellationToken cancellationToken = default);
    event EventHandler<string>? EngineErrorOccurred;
    event EventHandler<string>? EngineWarningOccurred;
    event EventHandler? AudioDeviceLost;
}

public interface IRecordingAudioLevelSource
{
    (AudioLevelSample? SystemAudio, AudioLevelSample? Microphone) ReadInputLevels(DateTimeOffset now);
}

public interface IBoundRecordingOutput
{
    // Caller owns the exclusively-created destination and keeps it open until Stop/Dispose.
    Stream? OutputStream { get; set; }
    bool UsesDirectPreview => false;
}

public class FFmpegScreenRecorderEngine : IScreenRecorderEngine, IRecordingAudioLevelSource, IBoundRecordingOutput, INativePointerCapture
{
    public bool UsesDirectPreview => _livePreviewOutput;
    public Stream? OutputStream { get; set; }
    private Task? _outputCopyTask;
    private RecordingPerformance _performance = new();
    private object? _performanceContext;
    private long _performanceStarted;
    private double _lastPerformanceSeconds;
    private Stream? _systemPcm;
    private Stream? _microphonePcm;
    private readonly IFFmpegPlatformProvider _ffmpegPlatformProvider;
    private readonly bool _livePreviewOutput;
    private readonly ISystemAudioLoopbackCapture? _systemAudioLoopbackCapture;
    private readonly IMicrophoneCapture? _microphoneCapture;
    private readonly IMicrophoneLevelObserver? _microphoneLevelObserver;
    private bool _systemLevelActive;
    private bool _microphoneLevelActive;
    private readonly string _ffmpegPath;
    private readonly IEncoderSelectionService _encoderSelectionService;
    private readonly HardwareEncoderType? _pinnedEncoder;
    private readonly CaptureSelection? _pinnedCapture;
    private readonly ICapturePlanProvider? _capturePlanProvider;
    private CaptureLaunchPlan? _capturePlan;
    private INativeVideoCapture? _nativeVideo;
    private NativeVideoPipe? _nativeVideoPipe;
    private volatile bool _nativeStopping;
    private bool _nativeCleanupFailed;
    public event EventHandler<NativePointerEvent>? PointerMetadata;
    public event EventHandler<string>? PointerIncomplete;
    private void OnNativePointerMetadata(object? sender, NativePointerEvent metadata) => PointerMetadata?.Invoke(this, metadata);
    private void OnNativePointerIncomplete(object? sender, string reason) =>
        PointerIncomplete?.Invoke(this, "Native pointer metadata is incomplete.");
    private void OnNativeVideoPipeFailed(object? sender, EventArgs args) =>
        OnNativeCaptureFailed(sender, "Native video pipe transport failed.");
    private void OnNativeCaptureFailed(object? sender, string error)
    {
        if (_nativeStopping) return;
        // A video-pipe EOF alone would allow the muxer to keep recording audio.
        // Fail closed and retain the already-written recovery MKV instead.
        lock (_lock)
        {
            _startupError = error;
            _startupSignal?.TrySetResult(false);
            try { if (_process is { HasExited: false } process) process.Kill(true); }
            catch (Exception ex) { Log.Error(ex, "Cannot terminate muxer after native video failure"); }
        }
        EngineErrorOccurred?.Invoke(this, error);
    }
    private string _startupError = "";
    public CaptureSelection? ActiveCapture { get; private set; }
    private Process? _process;
    private Task? _stderrReadingTask;
    private TimeSpan _recordedTime = TimeSpan.Zero;
    private long _framesRecorded = 0;
    private bool _isDisposed;
    private readonly object _lock = new();
    private TaskCompletionSource<bool>? _startupSignal;
    private readonly Queue<string> _stderrTail = new();

    private const int StderrTailLimit = 40;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MacOsAudioStartupTimeout = TimeSpan.FromSeconds(15);

    public bool UseSyntheticCaptureSource { get; set; }
    public HardwareEncoderType ActiveEncoder { get; private set; } = HardwareEncoderType.SoftwareCpu;

    private static readonly Regex TimeRegex = new(@"time=(\d{2}):(\d{2}):(\d{2}\.\d+)", RegexOptions.Compiled);
    private static readonly Regex FrameRegex = new(@"frame=\s*(\d+)", RegexOptions.Compiled);

    public bool IsRunning => _process != null && !_process.HasExited;
    public TimeSpan CurrentRecordedTime => _recordedTime;
    public long CurrentFramesRecorded => _framesRecorded;

    public (AudioLevelSample? SystemAudio, AudioLevelSample? Microphone) ReadInputLevels(DateTimeOffset now)
    {
        if (!IsRunning)
        {
            return (null, null);
        }

        AudioLevelSample? system = null;
        AudioLevelSample? microphone = null;
        try
        {
            if (_systemLevelActive && _systemAudioLoopbackCapture is IAudioLevelSource source)
            {
                system = source.ReadLatestLevel(now);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "讀取系統音訊即時音量失敗");
        }
        try
        {
            if (_microphoneLevelActive)
            {
                microphone = (_microphoneCapture as IAudioLevelSource)?.ReadLatestLevel(now) ??
                    _microphoneLevelObserver?.ReadLatestLevel(now);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "讀取麥克風即時音量失敗");
        }
        return (system, microphone);
    }

    public event EventHandler<string>? EngineErrorOccurred;
    public event EventHandler<string>? EngineWarningOccurred;
    public event EventHandler? AudioDeviceLost;

    public FFmpegScreenRecorderEngine(
        IFFmpegPlatformProvider ffmpegPlatformProvider, 
        ISystemAudioLoopbackCapture? systemAudioLoopbackCapture = null,
        string? ffmpegPath = null,
        IMicrophoneCapture? microphoneCapture = null,
        IMicrophoneLevelObserver? microphoneLevelObserver = null,
        IEncoderSelectionService? encoderSelectionService = null,
        HardwareEncoderType? pinnedEncoder = null,
        ICapturePlanProvider? capturePlanProvider = null,
        CaptureSelection? pinnedCapture = null,
        bool livePreviewOutput = false)
    {
        _ffmpegPlatformProvider = ffmpegPlatformProvider ?? throw new ArgumentNullException(nameof(ffmpegPlatformProvider));
        _livePreviewOutput = livePreviewOutput;
        _systemAudioLoopbackCapture = systemAudioLoopbackCapture;
        _microphoneCapture = microphoneCapture;
        _microphoneLevelObserver = microphoneLevelObserver;
        _ffmpegPath = ffmpegPath ?? FFmpegDiscovery.FindFFmpegExecutable()
            ?? throw new FileNotFoundException("未在系統中探測到 FFmpeg 執行檔");
        if (pinnedEncoder.HasValue && (pinnedEncoder == HardwareEncoderType.Auto || !Enum.IsDefined(pinnedEncoder.Value)))
            throw new ArgumentOutOfRangeException(nameof(pinnedEncoder));
        _pinnedEncoder = pinnedEncoder;
        _pinnedCapture = pinnedCapture;
        _capturePlanProvider = capturePlanProvider;
        _encoderSelectionService = encoderSelectionService ?? new FFmpegEncoderDetector(ffmpegPlatformProvider, _ffmpegPath);

        if (_microphoneCapture != null)
        {
            _microphoneCapture.AudioErrorOccurred += OnMicrophoneCaptureError;
        }
    }

    public async Task StartRecordingAsync(
        string workingFilePath, 
        RecordingConfiguration config, 
        CaptureRegion actualBounds, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            await StartRecordingCoreAsync(workingFilePath, config, actualBounds, cancellationToken);
        }
        catch
        {
            // A failed FFmpeg handshake must not leave the native FIFO producers
            // running; the next attempt uses the same capture service instance.
            try { await StopRecordingAsync(CancellationToken.None); }
            catch (Exception cleanupError)
            {
                Log.Warning(cleanupError, "清理錄影啟動失敗的音訊擷取時發生錯誤");
            }
            throw;
        }
    }

    private async Task StartRecordingCoreAsync(
        string workingFilePath,
        RecordingConfiguration config,
        CaptureRegion actualBounds,
        CancellationToken cancellationToken)
    {
        if (_livePreviewOutput && OutputStream is null)
            throw new InvalidOperationException("Live preview requires a bound streaming output.");
        _systemPcm = null;
        _microphonePcm = null;
        _systemLevelActive = false;
        _microphoneLevelActive = false;
        lock (_lock)
        {
            if (_nativeCleanupFailed)
                throw new InvalidOperationException("Previous native capture cleanup was not confirmed; create a new recording engine.");
            if (IsRunning)
            {
                throw new InvalidOperationException("錄影引擎已在運作中");
            }

            // 確保重設計時與幀數統計
            _recordedTime = TimeSpan.Zero;
            _framesRecorded = 0;

            var dir = Path.GetDirectoryName(workingFilePath);
            if (OutputStream is null && !string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        // H.264 要求寬度和高度必須為偶數 (divisible by 2)
        int width = actualBounds.Width;
        int height = actualBounds.Height;
        if (width % 2 != 0) width--;
        if (height % 2 != 0) height--;

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException($"錄影範圍尺寸無效: {actualBounds.Width}x{actualBounds.Height}");
        }

        if (OutputStream is null && (Path.Exists(workingFilePath) || new FileInfo(workingFilePath).LinkTarget != null))
            throw new IOException("Recording output already exists; refusing to overwrite it.");
        // Select before opening native PCM producers: probing must not fill their pipes.
        var nativeProvider = !_livePreviewOutput && !UseSyntheticCaptureSource && config.NativeCaptureEnabled &&
            _pinnedCapture?.FallbackReason != CaptureFallbackReason.StartupFailed &&
            _ffmpegPlatformProvider is INativeVideoPlatformProvider candidate &&
            (!candidate.RequiresEditablePointer || config.EditablePointerEnabled) ? candidate : null;
        var targetEncoder = nativeProvider?.RequiredNativeEncoder is { } required ? required
            : _pinnedEncoder ?? (await _encoderSelectionService.SelectAsync(config.EncoderType, cancellationToken)).Encoder;
        cancellationToken.ThrowIfCancellationRequested();

        ActiveCapture = null;
        _startupError = "";
        _capturePlan = nativeProvider is null && !UseSyntheticCaptureSource && _capturePlanProvider != null
            ? await _capturePlanProvider.PrepareAsync(config,
                new(actualBounds.X, actualBounds.Y, width, height), _pinnedCapture, cancellationToken)
            : null;
        // Prepare native video before starting PCM producers: permissions/encoder setup
        // must not add seconds of buffered audio before the first video frame.
        if (nativeProvider is not null)
        {
            if (_pinnedEncoder is not null && _pinnedEncoder != targetEncoder)
                throw new InvalidOperationException("Native capture cannot change the pinned encoder.");
            _nativeVideo = await nativeProvider.PrepareNativeVideoAsync(config,
                new(actualBounds.X, actualBounds.Y, width, height), _pinnedCapture, cancellationToken);
            _nativeVideo.CaptureFailed += OnNativeCaptureFailed;
            if (_nativeVideo is INativePointerCapture pointer)
            {
                pointer.PointerMetadata += OnNativePointerMetadata;
                pointer.PointerIncomplete += OnNativePointerIncomplete;
            }
        }
        _performance = new();
        _performanceStarted = 0;
        _lastPerformanceSeconds = 0;
        _performanceContext = new
        {
            MeasurementId = Guid.NewGuid(),
            RequestedCapture = config.WindowsCaptureMode.ToString(),
            ActualCapture = _nativeVideo?.Selection.Backend.ToString() ?? _capturePlan?.Selection.Backend.ToString() ?? "PlatformDefault",
            Fallback = _capturePlan?.Selection.FallbackReason.ToString() ?? "None",
            RequestedFps = config.Fps, Width = width, Height = height,
            Encoder = targetEncoder.ToString(), BoundOutput = OutputStream is not null,
            Synthetic = UseSyntheticCaptureSource
        };

        var requestsMicrophone =
            config.AudioSource == AudioSourceType.MicrophoneOnly ||
            config.AudioSource == AudioSourceType.SystemAndMicrophone;
        var hasDirectShowMic = requestsMicrophone
                               && !string.IsNullOrWhiteSpace(config.MicrophoneDeviceId)
                               && config.MicrophoneDeviceId != "default_mic";

        string? microphoneAudioPipeArg = null;
        var useNativeMicrophone =
            requestsMicrophone &&
            !UseSyntheticCaptureSource &&
            !config.IsRecoverySilenceMode &&
            _microphoneCapture?.IsSupported == true;

        if (useNativeMicrophone)
        {
            try
            {
                var microphoneInfo = await _microphoneCapture!.StartCaptureAsync(
                    config.MicrophoneDeviceId,
                    cancellationToken);
                if (microphoneInfo != null)
                {
                    microphoneAudioPipeArg = microphoneInfo.FfmpegInputArgs;
                    _microphonePcm = microphoneInfo.PcmStream;
                    _microphoneLevelActive = _microphoneCapture is IAudioLevelSource;
                    hasDirectShowMic = false;
                    Log.Information(
                        "原生麥克風擷取已啟動，傳遞參數: {Args}",
                        microphoneAudioPipeArg);
                }
                else
                {
                    hasDirectShowMic = false;
                    EngineWarningOccurred?.Invoke(
                        this,
                        "原生麥克風擷取初始化失敗，已回退至靜音軌以維持畫面錄製。");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                hasDirectShowMic = false;
                Log.Warning(ex, "啟動原生麥克風擷取失敗，回退至靜音軌以確保畫面錄影不受影響");
                EngineWarningOccurred?.Invoke(
                    this,
                    $"麥克風擷取初始化失敗 ({ex.Message})，已自動維持畫面錄影。");
            }
        }

        // 音訊設備健康防護：若指定之麥克風離線，安全回退至虛擬音軌以防止進程崩潰
        if (hasDirectShowMic && !UseSyntheticCaptureSource && !useNativeMicrophone)
        {
            bool isDevAvailable = IsAudioDeviceAvailable(_ffmpegPath, config.MicrophoneDeviceId!);
            if (!isDevAvailable)
            {
                EngineWarningOccurred?.Invoke(this, $"指定麥克風 \"{config.MicrophoneDeviceId}\" 離線或找不到，已自動安全回退至虛擬音軌以維持畫面錄製。");
                hasDirectShowMic = false;
            }
        }

        // 系統聲音擷取 (WASAPI Loopback)：若開啟系統聲音，啟動 PCM 即時音訊管道
        string? systemAudioPipeArg = null;
        if ((config.AudioSource == AudioSourceType.SystemOnly || config.AudioSource == AudioSourceType.SystemAndMicrophone)
            && !UseSyntheticCaptureSource && _systemAudioLoopbackCapture != null && _systemAudioLoopbackCapture.IsSupported)
        {
            try
            {
                var sysAudioInfo = await _systemAudioLoopbackCapture.StartCaptureAsync(
                    config.MonitorIndex,
                    cancellationToken);
                if (sysAudioInfo != null)
                {
                    systemAudioPipeArg = sysAudioInfo.FfmpegInputArgs;
                    _systemPcm = sysAudioInfo.PcmStream;
                    _systemLevelActive = _systemAudioLoopbackCapture is IAudioLevelSource;
                    Log.Information("系統聲音 Loopback 擷取已啟動，傳遞參數: {Args}", systemAudioPipeArg);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Log.Warning(ex, "啟動系統音訊 Loopback 擷取失敗，回退至靜音軌以確保畫面錄影不受影響");
                EngineWarningOccurred?.Invoke(this, $"系統聲音擷取初始化失敗 ({ex.Message})，已自動維持畫面錄影。");
            }
        }

        // One attempt only. The orchestrator owns fresh-path fallback before the first segment.
        bool launched = await LaunchProcessAsync(
            workingFilePath,
            config,
            actualBounds.X,
            actualBounds.Y,
            width,
            height,
            hasDirectShowMic,
            targetEncoder,
            systemAudioPipeArg,
            microphoneAudioPipeArg,
            cancellationToken);

        if (!launched)
        {
            if (config.NativeCaptureEnabled && !UseSyntheticCaptureSource)
                throw new InvalidOperationException("Native capture could not start; select compatible capture or inspect recording diagnostics.");
            if (_capturePlan?.Selection.Backend == CaptureBackend.DesktopDuplication)
            {
                // Only identified DDA initialization failures may change capture backend.
                if (_startupError.Split('\n').Any(line =>
                    Regex.IsMatch(line, @"^\[(?:Parsed_)?ddagrab(?:_\d+)?\s*@[^\]]*\]",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                    (line.Contains("Failed to", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Error initializing", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("DuplicateOutput failed", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Selected output not supported", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Too many open duplication sessions", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Failed duplicating output", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Failed querying IDXGIDevice", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Failed getting parent IDXGIAdapter", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Failed getting output description", StringComparison.OrdinalIgnoreCase))))
                    throw new CaptureStartupException("Desktop Duplication initialization failed.");
                throw new InvalidOperationException("Modern capture could not start; see recording diagnostics. No automatic backend switch was made.");
            }
            if (_capturePlan?.Selection.FallbackReason is not null and not CaptureFallbackReason.None &&
                !_startupError.Contains("Error while opening encoder", StringComparison.OrdinalIgnoreCase) &&
                !_startupError.Contains("Error initializing encoder", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Compatible capture fallback failed; no further encoder retry was made.");
            throw new EncoderStartupException("FFmpeg 錄影引擎初始化失敗，無法啟動錄影進程。請檢查視訊/音訊設備與輸出路徑權限。");
        }

        ActiveEncoder = targetEncoder;
        ActiveCapture = _nativeVideo?.Selection ?? _capturePlan?.Selection;
        if (hasDirectShowMic && _microphoneLevelObserver != null &&
            !string.IsNullOrWhiteSpace(config.MicrophoneDeviceId))
        {
            try
            {
                _microphoneLevelActive = await _microphoneLevelObserver.StartAsync(
                    config.MicrophoneDeviceId, cancellationToken);
            }
            catch
            {
                _microphoneLevelActive = false;
            }
        }
    }

    private async Task<bool> LaunchProcessAsync(
        string workingFilePath,
        RecordingConfiguration config,
        int x, int y, int width, int height,
        bool hasDirectShowMic,
        HardwareEncoderType encoderType,
        string? systemAudioPipeArg,
        string? microphoneAudioPipeArg,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_process != null)
            {
                try { if (!_process.HasExited) _process.Kill(true); } catch { }
                _process.Dispose();
                _process = null;
            }

            using var systemDescriptor = InheritedAudioDescriptor.Duplicate(_systemPcm);
            using var microphoneDescriptor = InheritedAudioDescriptor.Duplicate(_microphonePcm);
            var nativeProvider = _nativeVideo is not null
                ? _ffmpegPlatformProvider as INativeVideoPlatformProvider : null;
            using var videoDescriptor = nativeProvider?.UsesNamedPipeVideo == false
                ? InheritedAudioDescriptor.Duplicate(_nativeVideo?.Output) : null;
            if (nativeProvider?.UsesNamedPipeVideo == true)
            {
                _nativeVideoPipe = new NativeVideoPipe();
                _nativeVideoPipe.CopyFailed += OnNativeVideoPipeFailed;
            }
            systemAudioPipeArg = systemDescriptor?.Input(2) ?? systemAudioPipeArg;
            microphoneAudioPipeArg = microphoneDescriptor?.Input(1) ?? microphoneAudioPipeArg;
            var inputArgs = nativeProvider is not null
                ? nativeProvider.BuildNativeInput(config, _nativeVideoPipe?.VideoInput ?? videoDescriptor!.VideoInput(), hasDirectShowMic,
                    systemAudioPipeArg, microphoneAudioPipeArg)
                : _livePreviewOutput
                ? _ffmpegPlatformProvider.BuildDirectPreviewArguments(config, x, y, width, height,
                    UseSyntheticCaptureSource, hasDirectShowMic, systemAudioPipeArg, microphoneAudioPipeArg,
                    _capturePlan?.VideoInputArguments)
                : _capturePlan != null
                ? _capturePlanProvider!.BuildInputArguments(_capturePlan, config, hasDirectShowMic, systemAudioPipeArg, microphoneAudioPipeArg)
                : _ffmpegPlatformProvider.BuildInputArguments(
                config,
                x,
                y,
                width,
                height,
                UseSyntheticCaptureSource,
                hasDirectShowMic,
                systemAudioPipeArg,
                microphoneAudioPipeArg);
            var outputPath = OutputStream is null ? workingFilePath : "pipe:1";
            var outputArgs = nativeProvider is not null ? nativeProvider.BuildNativeOutput(config, encoderType, outputPath)
                : _livePreviewOutput ? "" : _ffmpegPlatformProvider.BuildOutputArguments(config, encoderType, outputPath);
            // Each attempt has its own fresh path. Refuse overwrite even if a
            // file appears after the caller's existence check.
            // FFmpeg's interactive stats use carriage-return updates on some
            // Windows builds, which ReadLineAsync cannot observe reliably.
            // The progress protocol is newline-delimited on every platform.
            var args = $"-n -progress pipe:2 -nostats {inputArgs} {outputArgs}";

            Log.Information("啟動 FFmpeg 錄影程序，完整引數: {Args}", args);

            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = OutputStream is not null,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            var proc = new Process { StartInfo = startInfo };
            _performanceStarted = Stopwatch.GetTimestamp();
            LogPerformance("Starting");
            proc.Start();
            if (OutputStream is not null)
                _outputCopyTask = CopyOutputAsync(proc, OutputStream);
            systemDescriptor?.Dispose();
            microphoneDescriptor?.Dispose();
            videoDescriptor?.Dispose();

            lock (_lock)
            {
                _process = proc;
                _startupSignal = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _stderrTail.Clear();
            }

            // Begin draining immediately: scheduling a blocking pipe reader on
            // the pool can let the startup deadline win before progress is read.
            _stderrReadingTask = ReadStderrLoop(proc, proc.StandardError, cancellationToken);
            if (_nativeVideo is not null)
            {
                if (_nativeVideoPipe is not null) await _nativeVideoPipe.StartAsync(_nativeVideo, cancellationToken);
                else await _nativeVideo.BeginAsync(cancellationToken);
            }

            var exitTask = proc.WaitForExitAsync(cancellationToken);
            var startupTimeout = OperatingSystem.IsMacOS() &&
                (microphoneAudioPipeArg != null || systemAudioPipeArg != null)
                ? MacOsAudioStartupTimeout
                : StartupTimeout;
            var timeoutTask = Task.Delay(startupTimeout, cancellationToken);
            if (await WaitForStartupConfirmationAsync(_startupSignal.Task, exitTask, timeoutTask, cancellationToken))
            {
                return true;
            }

            Log.Warning("FFmpeg startup rejected before termination: progress={ProgressStatus}, frames={Frames}, exited={Exited}, deadline={Deadline}",
                _startupSignal.Task.Status, _framesRecorded, exitTask.IsCompleted, timeoutTask.IsCompleted);

            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(true);
                }
            }
            catch { }

            try { await proc.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)); }
            catch { _nativeCleanupFailed |= _nativeVideo is not null; }
            if (_stderrReadingTask != null)
            {
                try { await _stderrReadingTask.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch { _nativeCleanupFailed |= _nativeVideo is not null; }
            }

            string errorTail;
            lock (_lock)
            {
                errorTail = string.Join(Environment.NewLine, _stderrTail);
                _startupError = errorTail;
                if (ReferenceEquals(_process, proc))
                {
                    _process = null;
                }
            }

            Log.Warning("FFmpeg 未在期限內產生第一個影格: {Reason}", errorTail);
            proc.Dispose();
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "啟動錄影進程遭遇例外");
            EngineErrorOccurred?.Invoke(this, $"啟動錄影程序失敗: {ex.Message}");
            return false;
        }
    }

    private static bool IsAudioDeviceAvailable(string ffmpegPath, string deviceName)
    {
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            if (!isWindows && !OperatingSystem.IsMacOS())
            {
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = BuildAudioDeviceListArguments(isWindows),
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardError.ReadToEnd();
                proc.WaitForExit(2000);
                return IsAudioDeviceListed(output, deviceName, isWindows);
            }
        }
        catch { }
        return false;
    }

    internal static string BuildAudioDeviceListArguments(bool isWindows) =>
        isWindows
            ? "-list_devices true -f dshow -i dummy"
            : "-hide_banner -list_devices true -f avfoundation -i \"\"";

    internal static bool IsAudioDeviceListed(
        string output,
        string deviceName,
        bool isWindows)
    {
        if (isWindows)
        {
            return output.Contains(deviceName, StringComparison.OrdinalIgnoreCase);
        }

        var audioSectionStart = output.IndexOf(
            "AVFoundation audio devices:",
            StringComparison.OrdinalIgnoreCase);
        if (audioSectionStart < 0)
        {
            return false;
        }

        var audioSection = output[audioSectionStart..];
        return audioSection.Contains(
            $"[{deviceName}]",
            StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<bool> WaitForStartupConfirmationAsync(
        Task<bool> firstFrame, Task exited, Task deadline, CancellationToken cancellationToken)
    {
        await Task.WhenAny(firstFrame, exited, deadline);
        cancellationToken.ThrowIfCancellationRequested();
        // A busy scheduler can resume us after both the deadline and real
        // progress are ready. Judge current evidence, not which callback queued
        // first. An observed exit or missing/false progress rejects startup.
        return !exited.IsCompleted && firstFrame.IsCompleted && await firstFrame;
    }

    internal async Task ReadStderrLoop(
        Process process,
        StreamReader stderr,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // EndOfStream performs a synchronous read when its buffer is
                // empty. ReadLineAsync already reports EOF with null.
                var line = await stderr.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null) break;
                _performance.ReadLine(line);
                if (line.StartsWith("progress=", StringComparison.Ordinal)) LogPerformance("Progress");

                lock (_lock)
                {
                    _stderrTail.Enqueue(line);
                    while (_stderrTail.Count > StderrTailLimit)
                    {
                        _stderrTail.Dequeue();
                    }
                }

                if (line.Contains("real-time buffer too full", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("error capturing audio", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("audio device lost", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("device disconnected", StringComparison.OrdinalIgnoreCase))
                {
                    AudioDeviceLost?.Invoke(this, EventArgs.Empty);
                }

                if (line.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug("[FFmpeg Stderr] {Line}", line);
                }

                var timeMatch = TimeRegex.Match(line);
                if (timeMatch.Success &&
                    int.TryParse(timeMatch.Groups[1].Value, out var hours) &&
                    int.TryParse(timeMatch.Groups[2].Value, out var minutes) &&
                    double.TryParse(timeMatch.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    _recordedTime = new TimeSpan(0, hours, minutes, (int)seconds, (int)((seconds - (int)seconds) * 1000));
                    if (seconds > 0 || hours > 0 || minutes > 0)
                    {
                        _startupSignal?.TrySetResult(true);
                    }
                }

                var frameMatch = FrameRegex.Match(line);
                if (frameMatch.Success && long.TryParse(frameMatch.Groups[1].Value, out var frames))
                {
                    _framesRecorded = frames;
                    if (frames > 0)
                    {
                        _startupSignal?.TrySetResult(true);
                    }
                }
            }

            _startupSignal?.TrySetResult(false);

            // 若在未主動取消的情況下 stderr 提前結束且進程退出碼異常，主動上報非預期終止
            if (!cancellationToken.IsCancellationRequested && process.HasExited && process.ExitCode != 0)
            {
                Log.Error("FFmpeg 進程於錄影中非預期中斷退出，ExitCode: {ExitCode}", process.ExitCode);
                EngineErrorOccurred?.Invoke(this, $"FFmpeg 錄影核心非預期中斷 (ExitCode: {process.ExitCode})");
            }
        }
        catch (OperationCanceledException)
        {
            _startupSignal?.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            _startupSignal?.TrySetException(ex);
            EngineErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            // Normal stop detaches this field before joining stderr. On any
            // other EOF/cancellation, do not leave ScreenCaptureKit capturing
            // into an abandoned pipe, even if the muxer exited with code zero.
            var native = _nativeVideo;
            if (native is not null && !_nativeStopping)
            {
                try { await native.StopAsync().WaitAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false); }
                catch (Exception ex) { Log.Error(ex, "Native video cleanup after muxer exit failed"); }
                if (!cancellationToken.IsCancellationRequested)
                    EngineErrorOccurred?.Invoke(this, "Native video muxer ended before recording was stopped.");
            }
        }
    }

    public async Task StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        Exception? nativeShutdownError = null;
        _systemLevelActive = false;
        _microphoneLevelActive = false;
        if (_microphoneLevelObserver is not null)
        {
            try { await _microphoneLevelObserver.StopAsync(cancellationToken); } catch { }
        }
        Process? proc;
        lock (_lock)
        {
            proc = _process;
        }

        // Close FIFO producers before asking FFmpeg to stop. If FFmpeg closes
        // its read ends first, the native audio helpers observe a broken pipe
        // and incorrectly report a normal recording stop as a capture error.
        if (_systemAudioLoopbackCapture != null)
        {
            try { await _systemAudioLoopbackCapture.StopCaptureAsync(cancellationToken); } catch { }
        }

        if (_microphoneCapture != null)
        {
            try
            {
                await _microphoneCapture.StopCaptureAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "停止原生麥克風擷取時發生錯誤");
            }
            _microphoneCapture.AudioErrorOccurred -= OnMicrophoneCaptureError;
        }

        try
        {
            if (_nativeVideo is not null)
            {
                var video = _nativeVideo;
                _nativeStopping = true;
                try
                {
                    await video.StopAsync().WaitAsync(TimeSpan.FromSeconds(12));
                    if (_nativeVideoPipe is not null) await _nativeVideoPipe.CompleteAsync();
                }
                catch (Exception ex)
                {
                    nativeShutdownError = ex;
                    _nativeCleanupFailed = true;
                    OnNativePointerIncomplete(this, "");
                    Log.Error(ex, "Native video producer did not stop cleanly");
                    EngineErrorOccurred?.Invoke(this, "Native capture shutdown failed; retain the recording for recovery.");
                }
                finally
                {
                    // Final pointer/clock records are delivered by StopAsync before detaching.
                    video.CaptureFailed -= OnNativeCaptureFailed;
                    if (video is INativePointerCapture pointer)
                    {
                        pointer.PointerMetadata -= OnNativePointerMetadata;
                        pointer.PointerIncomplete -= OnNativePointerIncomplete;
                    }
                    try { await video.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4)); }
                    catch (Exception ex)
                    {
                        nativeShutdownError ??= ex;
                        _nativeCleanupFailed = true;
                        OnNativePointerIncomplete(this, "");
                        Log.Error("Native video resource cleanup could not be confirmed.");
                    }
                    _nativeVideo = null;
                    _nativeStopping = false;
                }
            }
            if (proc != null && !proc.HasExited)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(6000);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    await proc.StandardInput.WriteAsync("q\n".AsMemory(), linkedCts.Token);
                    await proc.StandardInput.FlushAsync(linkedCts.Token);
                    proc.StandardInput.Close();
                    await proc.WaitForExitAsync(linkedCts.Token);
                }
                catch
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            proc.Kill(true);
                        }
                    }
                    catch { }
                }
            }
            if (proc is { HasExited: false })
            {
                try { await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch
                {
                    nativeShutdownError ??= new IOException("Recording muxer termination could not be confirmed.");
                    _nativeCleanupFailed = true;
                    OnNativePointerIncomplete(this, "");
                }
            }
        }
        finally
        {
            if (_nativeVideoPipe is not null)
            {
                var bridge = _nativeVideoPipe;
                _nativeVideoPipe = null;
                bridge.CopyFailed -= OnNativeVideoPipeFailed;
                try { await bridge.DisposeAsync(); }
                catch (Exception ex)
                {
                    nativeShutdownError ??= ex;
                    _nativeCleanupFailed = true;
                    OnNativePointerIncomplete(this, "");
                    Log.Error("Native video pipe cleanup could not be confirmed.");
                }
            }
            if (_stderrReadingTask != null)
            {
                try { await _stderrReadingTask.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException)
                {
                    nativeShutdownError ??= new IOException("Recording diagnostics did not close within the stop deadline.");
                    _nativeCleanupFailed = true;
                    OnNativePointerIncomplete(this, "");
                }
                catch { }
            }

            try
            {
                if (_outputCopyTask is { } output)
                {
                    try
                    {
                        await output.WaitAsync(TimeSpan.FromSeconds(6));
                        _outputCopyTask = null;
                    }
                    catch (TimeoutException)
                    {
                        _nativeCleanupFailed = true;
                        OnNativePointerIncomplete(this, "");
                        throw new IOException("Recording output drain did not complete within the stop deadline; retain the working recording.");
                    }
                }
            }
            finally
            {
                LogPerformance("Stopped");
                _performanceStarted = 0;
                lock (_lock) { _process?.Dispose(); _process = null; }
            }
        }
        if (nativeShutdownError is not null)
            throw new IOException("Native video shutdown was not clean; retain the recording for recovery.", nativeShutdownError);
    }

    private async Task CopyOutputAsync(Process process, Stream destination)
    {
        try
        {
            var buffer = new byte[81920];
            int count;
            while ((count = await process.StandardOutput.BaseStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                var started = Stopwatch.GetTimestamp();
                await destination.WriteAsync(buffer.AsMemory(0, count)).ConfigureAwait(false);
                _performance.NoteWrite(count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            await destination.FlushAsync();
        }
        catch (Exception ex)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            EngineErrorOccurred?.Invoke(this, "Recording destination write failed: " + ex.Message);
            throw;
        }
    }

    private void LogPerformance(string phase)
    {
        if (_livePreviewOutput || _performanceStarted == 0) return;
        var elapsed = Stopwatch.GetElapsedTime(_performanceStarted).TotalSeconds;
        if (phase == "Progress" && elapsed - _lastPerformanceSeconds < 5) return;
        _lastPerformanceSeconds = elapsed;
        try
        {
            var snapshot = _performance.Snapshot(elapsed);
            if (OutputStream is null)
                snapshot = snapshot with { OutputBytes = null, WriteWaitMs = null, MaxWriteWaitMs = null };
            var measurement = JsonSerializer.Serialize(new
            {
                Phase = phase, Context = _performanceContext, Metrics = snapshot
            });
            Log.Information("Recording performance: {Measurement:l}", measurement);
        }
        catch (Exception ex)
        {
            Log.Debug("Cannot serialize recording performance: {ErrorType}", ex.GetType().Name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;

        await StopRecordingAsync();
        _isDisposed = true;

        if (_microphoneCapture != null)
        {
            _microphoneCapture.AudioErrorOccurred -= OnMicrophoneCaptureError;
        }

        lock (_lock)
        {
            _process?.Dispose();
            _process = null;
        }

        GC.SuppressFinalize(this);
    }

    private void OnMicrophoneCaptureError(object? sender, string error)
    {
        Log.Warning("原生麥克風擷取發生錯誤: {Error}", error);
        AudioDeviceLost?.Invoke(this, EventArgs.Empty);
        EngineWarningOccurred?.Invoke(this, error);
    }
}
