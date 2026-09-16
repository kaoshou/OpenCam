using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.FFmpeg;
using Serilog;

namespace ScreenRecorder.Media.Capture;

public interface IScreenRecorderEngine : IAsyncDisposable
{
    bool IsRunning { get; }
    bool UseSyntheticCaptureSource { get; set; }
    TimeSpan CurrentRecordedTime { get; }
    long CurrentFramesRecorded { get; }
    HardwareEncoderType ActiveEncoder { get; }
    Task StartRecordingAsync(string workingFilePath, RecordingConfiguration config, CaptureRegion actualBounds, CancellationToken cancellationToken = default);
    Task StopRecordingAsync(CancellationToken cancellationToken = default);
    event EventHandler<string>? EngineErrorOccurred;
    event EventHandler<string>? EngineWarningOccurred;
    event EventHandler? AudioDeviceLost;
}

public class FFmpegScreenRecorderEngine : IScreenRecorderEngine
{
    private readonly IFFmpegPlatformProvider _ffmpegPlatformProvider;
    private readonly ISystemAudioLoopbackCapture? _systemAudioLoopbackCapture;
    private readonly string _ffmpegPath;
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

    public bool UseSyntheticCaptureSource { get; set; }
    public HardwareEncoderType ActiveEncoder { get; private set; } = HardwareEncoderType.SoftwareCpu;

    private static readonly Regex TimeRegex = new(@"time=(\d{2}):(\d{2}):(\d{2}\.\d+)", RegexOptions.Compiled);
    private static readonly Regex FrameRegex = new(@"frame=\s*(\d+)", RegexOptions.Compiled);

    public bool IsRunning => _process != null && !_process.HasExited;
    public TimeSpan CurrentRecordedTime => _recordedTime;
    public long CurrentFramesRecorded => _framesRecorded;

    public event EventHandler<string>? EngineErrorOccurred;
    public event EventHandler<string>? EngineWarningOccurred;
    public event EventHandler? AudioDeviceLost;

    public FFmpegScreenRecorderEngine(
        IFFmpegPlatformProvider ffmpegPlatformProvider, 
        ISystemAudioLoopbackCapture? systemAudioLoopbackCapture = null,
        string? ffmpegPath = null)
    {
        _ffmpegPlatformProvider = ffmpegPlatformProvider ?? throw new ArgumentNullException(nameof(ffmpegPlatformProvider));
        _systemAudioLoopbackCapture = systemAudioLoopbackCapture;
        _ffmpegPath = ffmpegPath ?? FFmpegDiscovery.FindFFmpegExecutable()
            ?? throw new FileNotFoundException("未在系統中探測到 FFmpeg 執行檔");
    }

    public async Task StartRecordingAsync(
        string workingFilePath, 
        RecordingConfiguration config, 
        CaptureRegion actualBounds, 
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("錄影引擎已在運作中");
            }

            // 確保重設計時與幀數統計
            _recordedTime = TimeSpan.Zero;
            _framesRecorded = 0;

            var dir = Path.GetDirectoryName(workingFilePath);
            if (!string.IsNullOrEmpty(dir))
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

        var hasDirectShowMic = (config.AudioSource == AudioSourceType.MicrophoneOnly || config.AudioSource == AudioSourceType.SystemAndMicrophone)
                               && !string.IsNullOrWhiteSpace(config.MicrophoneDeviceId)
                               && config.MicrophoneDeviceId != "default_mic";

        // 音訊設備健康防護：若指定之麥克風離線，安全回退至虛擬音軌以防止進程崩潰
        if (hasDirectShowMic && !UseSyntheticCaptureSource)
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
                    Log.Information("系統聲音 Loopback 擷取已啟動，傳遞參數: {Args}", systemAudioPipeArg);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "啟動系統音訊 Loopback 擷取失敗，回退至靜音軌以確保畫面錄影不受影響");
                EngineWarningOccurred?.Invoke(this, $"系統聲音擷取初始化失敗 ({ex.Message})，已自動維持畫面錄影。");
            }
        }

        // 解析欲使用之編碼器
        var targetEncoder = config.EncoderType;
        if (targetEncoder == HardwareEncoderType.Auto)
        {
            targetEncoder = DetectBestHardwareEncoder();
        }

        // 啟動錄影進程（具備失敗自動安全 Fallback 機制）
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
            cancellationToken);
        if (!launched && targetEncoder != HardwareEncoderType.SoftwareCpu)
        {
            EngineWarningOccurred?.Invoke(this, $"硬體編碼器 ({targetEncoder}) 初始化失敗，已自動安全回退至 CPU 軟體編碼 (libx264)。");
            targetEncoder = HardwareEncoderType.SoftwareCpu;
            launched = await LaunchProcessAsync(
                workingFilePath,
                config,
                actualBounds.X,
                actualBounds.Y,
                width,
                height,
                hasDirectShowMic,
                targetEncoder,
                systemAudioPipeArg,
                cancellationToken);
        }

        if (!launched)
        {
            throw new InvalidOperationException("FFmpeg 錄影引擎初始化失敗，無法啟動錄影進程。請檢查視訊/音訊設備與輸出路徑權限。");
        }

        ActiveEncoder = targetEncoder;
    }

    private async Task<bool> LaunchProcessAsync(
        string workingFilePath,
        RecordingConfiguration config,
        int x, int y, int width, int height,
        bool hasDirectShowMic,
        HardwareEncoderType encoderType,
        string? systemAudioPipeArg,
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

            var inputArgs = _ffmpegPlatformProvider.BuildInputArguments(config, x, y, width, height, UseSyntheticCaptureSource, hasDirectShowMic, systemAudioPipeArg);
            var outputArgs = _ffmpegPlatformProvider.BuildOutputArguments(config, encoderType, workingFilePath);
            // A hardware encoder failure can leave an empty working file
            // before the software fallback starts. Keep fallback launches
            // non-interactive so FFmpeg never blocks on an overwrite prompt.
            var args = $"-y {inputArgs} {outputArgs}";

            Log.Information("啟動 FFmpeg 錄影程序，完整引數: {Args}", args);

            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            var proc = new Process { StartInfo = startInfo };
            proc.Start();

            lock (_lock)
            {
                _process = proc;
                _startupSignal = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _stderrTail.Clear();
            }

            _stderrReadingTask = Task.Run(
                () => ReadStderrLoop(proc, proc.StandardError, cancellationToken),
                CancellationToken.None);

            var exitTask = proc.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(StartupTimeout, cancellationToken);
            var completed = await Task.WhenAny(_startupSignal.Task, exitTask, timeoutTask);

            cancellationToken.ThrowIfCancellationRequested();
            if (completed == _startupSignal.Task && await _startupSignal.Task)
            {
                return true;
            }

            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(true);
                }
            }
            catch { }

            try { await proc.WaitForExitAsync(CancellationToken.None); } catch { }
            if (_stderrReadingTask != null)
            {
                try { await _stderrReadingTask; } catch { }
            }

            string errorTail;
            lock (_lock)
            {
                errorTail = string.Join(Environment.NewLine, _stderrTail);
                if (ReferenceEquals(_process, proc))
                {
                    _process = null;
                }
            }

            Log.Warning("FFmpeg 未在期限內產生第一個影格: {Reason}", errorTail);
            proc.Dispose();
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "啟動錄影進程遭遇例外");
            EngineErrorOccurred?.Invoke(this, $"啟動錄影程序失敗: {ex.Message}");
            return false;
        }
    }

    private HardwareEncoderType DetectBestHardwareEncoder()
    {
        var probes = _ffmpegPlatformProvider.GetHardwareEncoderProbes();
        foreach (var probe in probes)
        {
            if (QuickProbeEncoder(probe.EncoderName, probe.ExtraArgs))
                return probe.Type;
        }
        
        return HardwareEncoderType.SoftwareCpu;
    }

    private bool QuickProbeEncoder(string encoderName, string extraArgs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-y -f lavfi -i testsrc=size=256x256:rate=30 -t 0.05 -c:v {encoderName} {extraArgs} -f null -",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null && p.WaitForExit(1500))
            {
                return p.ExitCode == 0;
            }
        }
        catch { }
        return false;
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

    private async Task ReadStderrLoop(
        Process process,
        StreamReader stderr,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !stderr.EndOfStream)
            {
                var line = await stderr.ReadLineAsync(cancellationToken);
                if (line == null) break;

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
    }

    public async Task StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        Process? proc;
        lock (_lock)
        {
            proc = _process;
        }

        try
        {
            if (proc != null && !proc.HasExited)
            {
                try
                {
                    await proc.StandardInput.WriteAsync("q\n");
                    await proc.StandardInput.FlushAsync();
                    proc.StandardInput.Close();

                    using var timeoutCts = new CancellationTokenSource(6000);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

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
        }
        finally
        {
            if (_systemAudioLoopbackCapture != null)
            {
                try { await _systemAudioLoopbackCapture.StopCaptureAsync(cancellationToken); } catch { }
            }

            if (_stderrReadingTask != null)
            {
                try { await _stderrReadingTask; } catch { }
            }

            lock (_lock)
            {
                _process?.Dispose();
                _process = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await StopRecordingAsync();

        if (_systemAudioLoopbackCapture != null)
        {
            try { await _systemAudioLoopbackCapture.DisposeAsync(); } catch { }
        }

        lock (_lock)
        {
            _process?.Dispose();
            _process = null;
        }

        GC.SuppressFinalize(this);
    }
}
