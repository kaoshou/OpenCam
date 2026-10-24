// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using Serilog;

namespace ScreenRecorder.Media.Capture;

/// <summary>Owns a single isolated libobs process and drains its MKV into the recorder's exclusive output.</summary>
public sealed class ObsScreenRecorderEngine : IScreenRecorderEngine, IBoundRecordingOutput, IRecordingAudioLevelSource
{
    private readonly string _helperPath;
    private readonly string _runtimeRoot;
    private readonly Func<RecordingConfiguration, CaptureRegion, CaptureSelection?, MonitorInfo?> _resolveMonitor;
    private readonly HardwareEncoderType _encoder;
    private readonly CaptureSelection? _pinnedCapture;
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _stopTimeout;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _state = new();
    private Process? _process;
    private Task? _copyTask;
    private Task? _stderrTask;
    private Task? _completionTask;
    private CancellationTokenSource? _ioCancellation;
    private FileStream? _ownedOutput;
    private TaskCompletionSource<bool> _ready = NewSignal();
    private TaskCompletionSource<bool> _firstByte = NewSignal();
    private IOException? _failure;
    private bool _stoppedEvent;
    private volatile bool _stopping;
    private volatile bool _running;
    private bool _disposed;
    private bool _used;
    private long _frames;
    private long _mediaUs;
    private long _reportedBytes;
    private long _writtenBytes;
    private string? _setupErrorCode;
    private bool _copyFailed;
    private bool _cleanupTimedOut;
    private AudioLevelSample? _systemLevel;
    private AudioLevelSample? _microphoneLevel;
    private ObsCaptureRequest? _request;
    private readonly RecordingPerformance _performance = new();
    private long _performanceStarted;
    private double _lastPerformanceSeconds;
    private string _requestedCaptureMode = "";
    private bool _performanceBoundOutput;

    public ObsScreenRecorderEngine(string helperPath, string runtimeRoot,
        Func<RecordingConfiguration, CaptureRegion, CaptureSelection?, MonitorInfo?> resolveMonitor,
        HardwareEncoderType pinnedEncoder = HardwareEncoderType.SoftwareCpu, CaptureSelection? pinnedCapture = null,
        TimeSpan? startupTimeout = null, TimeSpan? stopTimeout = null)
    {
        _helperPath = Path.GetFullPath(helperPath);
        _runtimeRoot = Path.GetFullPath(runtimeRoot);
        _resolveMonitor = resolveMonitor ?? throw new ArgumentNullException(nameof(resolveMonitor));
        _ = ObsCaptureRequest.EncoderName(pinnedEncoder);
        _encoder = pinnedEncoder;
        _pinnedCapture = pinnedCapture;
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(15);
        _stopTimeout = stopTimeout ?? TimeSpan.FromSeconds(8);
        if (_startupTimeout <= TimeSpan.Zero || _stopTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(startupTimeout));
    }

    public Stream? OutputStream { get; set; }
    public bool UseSyntheticCaptureSource { get; set; }
    public bool IsRunning => _running;
    public TimeSpan CurrentRecordedTime => TimeSpan.FromTicks(Interlocked.Read(ref _mediaUs) * 10);
    public long CurrentFramesRecorded => Interlocked.Read(ref _frames);
    public HardwareEncoderType ActiveEncoder => _encoder;
    public CaptureSelection? ActiveCapture { get; private set; }
    public event EventHandler<string>? EngineErrorOccurred;
    public event EventHandler<string>? EngineWarningOccurred;
    public event EventHandler? AudioDeviceLost;

    public (AudioLevelSample? SystemAudio, AudioLevelSample? Microphone) ReadInputLevels(DateTimeOffset now)
    {
        lock (_state)
        {
            if (!_running) return (null, null);
            return (Fresh(_systemLevel, now), Fresh(_microphoneLevel, now));
        }
    }

    private static AudioLevelSample? Fresh(AudioLevelSample? value, DateTimeOffset now) =>
        value is { } sample && now - sample.CapturedAt <= TimeSpan.FromSeconds(2) ? sample : null;

    public async Task StartRecordingAsync(string workingFilePath, RecordingConfiguration config, CaptureRegion actualBounds,
        CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        var ownsAttempt = false;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_used) throw new InvalidOperationException("Create a fresh recording engine for each segment.");
            cancellationToken.ThrowIfCancellationRequested();
            ownsAttempt = true;
            var monitor = _resolveMonitor(config, actualBounds, _pinnedCapture)
                ?? throw new ArgumentException("The selected native display cannot be mapped exactly.");
            _request = ObsCaptureRequest.Create(config, actualBounds, monitor, _encoder, UseSyntheticCaptureSource);
            _requestedCaptureMode = config.WindowsCaptureMode.ToString();
            var requestBytes = _request.Serialize();
            if (_pinnedCapture is { } pinned && (pinned.RecordingCore != "libobs-v1" || pinned.DeviceName != monitor.DeviceName ||
                pinned.Bounds != actualBounds || pinned.OutputBounds != monitor.Bounds))
                throw new IOException("The resumed segment no longer matches its pinned recording source.");
            var destination = OutputStream;
            _performanceBoundOutput = destination is not null;
            if (destination is not null && !destination.CanWrite) throw new ArgumentException("Recording output must be writable.");
            if (destination is null)
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(workingFilePath));
                if (directory is not null) Directory.CreateDirectory(directory);
                _ownedOutput = new FileStream(workingFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                    81920, FileOptions.Asynchronous);
                destination = _ownedOutput;
            }
            _used = true;
            _ioCancellation = new CancellationTokenSource();
            _process = Process.Start(CreateProcessStartInfo()) ?? throw new IOException("Recording helper did not start.");
            _performanceStarted = Stopwatch.GetTimestamp();
            _copyTask = CopyOutputAsync(_process, destination, _ioCancellation.Token);
            _stderrTask = ReadEventsAsync(_process, monitor, _ioCancellation.Token);
            _completionTask = ObserveExitAsync(_process);
            await _process.StandardInput.BaseStream.WriteAsync(requestBytes, cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.BaseStream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(_ready.Task, _firstByte.Task).WaitAsync(_startupTimeout, cancellationToken).ConfigureAwait(false);
            lock (_state)
            {
                if (_failure is not null) throw _failure;
                if (_process.HasExited) throw new IOException("Recording helper exited during startup.");
                _running = true;
            }
            LogPerformance("Started");
        }
        catch (Exception error)
        {
            if (!ownsAttempt) throw;
            _stopping = true;
            KillHelper();
            await CleanupAsync().ConfigureAwait(false);
            LogPerformance("StartupFailed");
            _performanceStarted = 0;
            if (error is OperationCanceledException or ArgumentException or ObjectDisposedException or InvalidOperationException) throw;
            if (_pinnedCapture is null && !_ready.Task.IsCompletedSuccessfully && Interlocked.Read(ref _writtenBytes) == 0 &&
                !_copyFailed && !_cleanupTimedOut)
            {
                if (_setupErrorCode is "display_identity" or "audio_device_identity" or "capture_source" or
                    "capture_start_timeout" or "graphics_startup" or "runtime_module")
                    throw new CaptureStartupException("Native capture setup is unsupported; no recording bytes were written.",
                        OperatingSystem.IsWindows() ? CaptureBackend.Gdi : CaptureBackend.PlatformDefault);
                if (_setupErrorCode is "encoder_unavailable" or "encoder_start")
                    throw new EncoderStartupException("Native encoder setup failed; no recording bytes were written.");
            }
            throw new IOException("Recording helper startup failed; retain any existing recording bytes.",
                error is TimeoutException ? new IOException("Recording helper startup timed out.") : null);
        }
        finally { _lifecycle.Release(); }
    }

    internal ProcessStartInfo CreateProcessStartInfo()
    {
        var start = new ProcessStartInfo(_helperPath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false, true), WorkingDirectory = _runtimeRoot
        };
        start.ArgumentList.Add("--runtime");
        start.ArgumentList.Add(_runtimeRoot);
        foreach (var key in new[] { "OBS_PLUGINS_PATH", "OBS_PLUGINS_DATA_PATH", "DYLD_INSERT_LIBRARIES", "DYLD_LIBRARY_PATH",
            "DYLD_FRAMEWORK_PATH", "DYLD_FALLBACK_LIBRARY_PATH", "LD_PRELOAD", "LD_LIBRARY_PATH" })
            start.Environment.Remove(key);
        if (OperatingSystem.IsWindows())
            start.Environment["PATH"] = Path.Combine(_runtimeRoot, "bin", "64bit") + Path.PathSeparator + Environment.SystemDirectory;
        return start;
    }

    public async Task StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        // Acquiring cleanup ownership itself is not cancellable: a cancelled Stop still has to terminate its producer.
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_process is null) return;
            _stopping = true;
            _running = false;
            try
            {
                if (!_process.HasExited)
                {
                    await _process.StandardInput.WriteLineAsync("STOP").WaitAsync(_stopTimeout, cancellationToken).ConfigureAwait(false);
                    await _process.StandardInput.FlushAsync().WaitAsync(_stopTimeout, cancellationToken).ConfigureAwait(false);
                    _process.StandardInput.Close();
                }
                if (_completionTask is not null)
                    await _completionTask.WaitAsync(_stopTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Fail("stop_cancelled");
                await CleanupAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception error) when (error is IOException or TimeoutException or InvalidOperationException)
            {
                Fail("stop_failed");
            }
            var failure = _failure;
            await CleanupAsync().ConfigureAwait(false);
            if (failure is not null || _failure is not null)
                throw new IOException("Recording helper did not stop cleanly; retain the working recording.");
        }
        finally
        {
            LogPerformance("Stopped");
            _performanceStarted = 0;
            _lifecycle.Release();
        }
    }

    private async Task CopyOutputAsync(Process process, Stream destination, CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[81920];
            int count;
            while ((count = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                var writeStarted = Stopwatch.GetTimestamp();
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                _performance.NoteWrite(count, Stopwatch.GetElapsedTime(writeStarted).TotalMilliseconds);
                Interlocked.Add(ref _writtenBytes, count);
                _firstByte.TrySetResult(true);
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (!_stopping) Fail("output_eof");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { _copyFailed = true; Fail("output_failed"); }
    }

    private async Task ReadEventsAsync(Process process, MonitorInfo monitor, CancellationToken cancellationToken)
    {
        try
        {
            // StreamReader.ReadLineAsync would buffer an unbounded malformed native diagnostic line.
            var line = new StringBuilder();
            var buffer = new char[2048];
            var overlong = false;
            int count;
            while ((count = await process.StandardError.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                for (var index = 0; index < count; index++)
                {
                    var character = buffer[index];
                    if (character == '\n')
                    {
                        if (overlong) { Fail("protocol_invalid"); return; }
                        ParseEvent(line.ToString().TrimEnd('\r'), monitor);
                        line.Clear();
                        if (_failure is not null) return;
                    }
                    else if (line.Length < 65536) line.Append(character);
                    else overlong = true;
                }
            }
            if (line.Length != 0 || overlong) Fail("protocol_invalid");
            else if (!_stopping || !_stoppedEvent) Fail("diagnostics_eof");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { Fail("protocol_invalid"); }
    }

    private void ParseEvent(string line, MonitorInfo monitor)
    {
        if (!line.StartsWith("OCOBS ", StringComparison.Ordinal)) return; // Never surface raw libobs logs or device paths.
        using var json = JsonDocument.Parse(line.AsSpan(6).ToString());
        var value = json.RootElement;
        switch (value.GetProperty("event").GetString())
        {
            case "ready":
                if (_ready.Task.IsCompleted || _stoppedEvent || value.GetProperty("protocol").GetInt32() != 1 ||
                    value.GetProperty("encoder").GetString() != _request!.Encoder ||
                    value.TryGetProperty("monitor", out var reportedMonitor) && reportedMonitor.GetString() != _request.Monitor)
                    throw new JsonException();
                var backend = value.GetProperty("backend").GetString();
                if (UseSyntheticCaptureSource ? backend != "synthetic" : OperatingSystem.IsWindows()
                    ? backend is not ("wgc" or "dxgi") : backend != "sck") throw new JsonException();
                var captureBackend = backend switch { "wgc" => CaptureBackend.WindowsGraphicsCapture,
                    "dxgi" => CaptureBackend.DesktopDuplication, "sck" => CaptureBackend.ScreenCaptureKit,
                    _ => CaptureBackend.PlatformDefault };
                if (_pinnedCapture is not null && _pinnedCapture.Backend != captureBackend) throw new JsonException();
                ActiveCapture = new(captureBackend, CaptureFallbackReason.None, monitor.DeviceName, null, null,
                    new(_request.X, _request.Y, _request.Width, _request.Height))
                    { OutputBounds = monitor.Bounds, RecordingCore = "libobs-v1" };
                _ready.TrySetResult(true);
                break;
            case "progress":
                if (!_ready.Task.IsCompletedSuccessfully || _stoppedEvent) throw new JsonException();
                var frames = value.GetProperty("frames").GetInt64();
                var mediaUs = value.GetProperty("mediaUs").GetInt64();
                var bytes = value.GetProperty("bytes").GetInt64();
                var system = value.GetProperty("systemPeak").GetDouble();
                var microphone = value.GetProperty("microphonePeak").GetDouble();
                if (frames < Interlocked.Read(ref _frames) || mediaUs < Interlocked.Read(ref _mediaUs) ||
                    mediaUs > TimeSpan.MaxValue.Ticks / 10 || bytes < _reportedBytes || !double.IsFinite(system) ||
                    !double.IsFinite(microphone) || system is < 0 or > 1 || microphone is < 0 or > 1) throw new JsonException();
                var now = DateTimeOffset.UtcNow;
                var systemLevel = Level(value, "systemAgeUs", system, now);
                var microphoneLevel = Level(value, "microphoneAgeUs", microphone, now);
                lock (_state)
                {
                    _systemLevel = _request!.Audio is "system" or "both" ? systemLevel : null;
                    _microphoneLevel = _request.Audio is "microphone" or "both" ? microphoneLevel : null;
                }
                _reportedBytes = bytes;
                _performance.ReadNativeProgress(frames, mediaUs);
                Interlocked.Exchange(ref _mediaUs, mediaUs);
                Interlocked.Exchange(ref _frames, frames);
                LogPerformance("Progress");
                break;
            case "warning":
                if (!_ready.Task.IsCompletedSuccessfully || _stoppedEvent ||
                    value.GetProperty("code").GetString() != "audio_source_stalled" ||
                    value.GetProperty("source").GetString() is not ("system" or "microphone")) throw new JsonException();
                EngineWarningOccurred?.Invoke(this, value.GetProperty("source").GetString() == "system"
                    ? "System audio capture is stalled; video and other audio sources continue."
                    : "Microphone capture is stalled; video and other audio sources continue.");
                break;
            case "stopped":
                if (!_stopping || !_ready.Task.IsCompletedSuccessfully || _stoppedEvent) throw new JsonException();
                _stoppedEvent = true;
                break;
            case "error":
                var code = value.GetProperty("code").GetString();
                _setupErrorCode = code;
                if (code == "audio_device_lost")
                {
                    AudioDeviceLost?.Invoke(this, EventArgs.Empty);
                    EngineWarningOccurred?.Invoke(this, "A selected recording audio device was lost.");
                }
                Fail("capture_failed");
                break;
            default: throw new JsonException();
        }
    }

    private static AudioLevelSample? Level(JsonElement value, string ageKey, double peak, DateTimeOffset now)
    {
        var age = value.TryGetProperty(ageKey, out var property) ? property.GetInt64() : 0;
        if (age < -1) throw new JsonException();
        if (age == -1 || age > 2000000) return null;
        return new(peak, peak, now - TimeSpan.FromTicks(age * 10));
    }

    private void LogPerformance(string phase)
    {
        if (_performanceStarted == 0 || _request is null) return;
        var elapsed = Stopwatch.GetElapsedTime(_performanceStarted).TotalSeconds;
        if (phase == "Progress" && elapsed - _lastPerformanceSeconds < 5) return;
        _lastPerformanceSeconds = elapsed;
        try
        {
            var measurement = JsonSerializer.Serialize(new
            {
                Phase = phase,
                Context = new
                {
                    RecordingCore = "libobs-v1", RequestedCapture = _requestedCaptureMode,
                    ActualCapture = ActiveCapture?.Backend.ToString() ?? "Starting", RequestedFps = _request.Fps,
                    _request.Width, _request.Height, _request.Encoder, _request.Quality,
                    BoundOutput = _performanceBoundOutput, Synthetic = UseSyntheticCaptureSource, Failed = _failure is not null
                },
                Metrics = _performance.Snapshot(elapsed)
            });
            Log.Information("Recording performance: {Measurement:l}", measurement);
        }
        catch (Exception error)
        {
            Log.Debug("Cannot serialize native recording performance: {ErrorType}", error.GetType().Name);
        }
    }

    private async Task ObserveExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (_stderrTask is not null) await _stderrTask.ConfigureAwait(false);
            if (_copyTask is not null) await _copyTask.ConfigureAwait(false);
            if (process.ExitCode != 0 || !_stopping || !_stoppedEvent) Fail("process_exited");
        }
        catch (Exception) { Fail("process_failed"); }
        finally { _running = false; }
    }

    private void Fail(string code)
    {
        lock (_state)
        {
            if (_failure is not null) return;
            _failure = new IOException("Recording helper failed (" + code + "); retain the working recording.");
            _running = false;
            _ready.TrySetException(_failure);
            _firstByte.TrySetException(_failure);
        }
        KillHelper();
        try { EngineErrorOccurred?.Invoke(this, _failure.Message); }
        catch (Exception error) { Log.Warning("Recording error observer failed: {ErrorType}", error.GetType().Name); }
    }

    private void KillHelper()
    {
        try { if (_process is { HasExited: false } process) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Log.Warning("Recording helper termination failed: {ErrorType}", error.GetType().Name); }
    }

    private async Task CleanupAsync()
    {
        _running = false;
        KillHelper();
        if (_completionTask is not null)
        {
            try { await _completionTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                _cleanupTimedOut = true;
                _ioCancellation?.Cancel();
                Fail("output_drain_timeout");
                try { await _completionTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
                catch (TimeoutException) { Log.Warning("Recording helper output drain exceeded cleanup deadline."); }
            }
        }
        _ioCancellation?.Cancel();
        _ioCancellation?.Dispose();
        _ioCancellation = null;
        _process?.Dispose();
        _process = null;
        if (_ownedOutput is not null)
        {
            await _ownedOutput.DisposeAsync().ConfigureAwait(false);
            _ownedOutput = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try { await StopRecordingAsync().ConfigureAwait(false); }
        catch (IOException error) { Log.Warning("Recording helper cleanup retained failed recording: {ErrorType}", error.GetType().Name); }
        finally { _disposed = true; }
    }

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
