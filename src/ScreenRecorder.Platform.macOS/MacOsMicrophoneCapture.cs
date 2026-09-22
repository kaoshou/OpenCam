// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.macOS;

public sealed class MacOsMicrophoneCapture : IMicrophoneCapture
{
    private const string ReadyLine =
        "READY sample-rate=48000 channels=1 format=s16le";

    private readonly string _helperPath;
    private readonly Func<string> _createTempDirectory;
    private readonly Action<string> _createFifo;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _helperProcess;
    private CancellationTokenSource? _monitorCts;
    private Task? _stderrMonitorTask;
    private string? _captureDirectory;
    private bool _isCapturing;
    private bool _isDisposed;

    public MacOsMicrophoneCapture()
        : this(
            MacOsMicrophoneSupport.HelperPath(AppContext.BaseDirectory),
            CreateDefaultTempDirectory,
            UnixFifo.CreatePrivate)
    {
    }

    internal MacOsMicrophoneCapture(
        string helperPath,
        Func<string> createTempDirectory,
        Action<string> createFifo)
    {
        _helperPath = helperPath;
        _createTempDirectory = createTempDirectory;
        _createFifo = createFifo;
    }

    public bool IsSupported =>
        OperatingSystem.IsMacOS() &&
        MacOsMicrophoneSupport.IsSupported(
            Path.GetDirectoryName(_helperPath) ?? AppContext.BaseDirectory);

    public bool IsCapturing => _isCapturing;

    public event EventHandler<string>? AudioErrorOccurred;

    public async Task<MicrophoneCaptureInfo?> StartCaptureAsync(
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_isCapturing)
            {
                throw new InvalidOperationException(
                    "macOS microphone capture is already running.");
            }

            if (!OperatingSystem.IsMacOS() || !File.Exists(_helperPath))
            {
                return null;
            }

            _captureDirectory = _createTempDirectory();
            Directory.CreateDirectory(_captureDirectory);
            File.SetUnixFileMode(
                _captureDirectory,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
            var fifoPath = Path.Combine(_captureDirectory, "microphone.pcm");
            _createFifo(fifoPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = _helperPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--fifo");
            startInfo.ArgumentList.Add(fifoPath);

            _helperProcess = Process.Start(startInfo) ??
                throw new InvalidOperationException(
                    "Unable to launch OpenCam.Microphone.");

            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            while (true)
            {
                var line = await _helperProcess.StandardError.ReadLineAsync(
                    linkedCts.Token);
                if (line is null)
                {
                    throw new InvalidOperationException(
                        "OpenCam.Microphone exited before reporting readiness.");
                }

                if (line == ReadyLine)
                {
                    break;
                }

                if (line.StartsWith("ERROR ", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(line[6..]);
                }
            }

            _monitorCts = new CancellationTokenSource();
            _stderrMonitorTask = MonitorStderrAsync(
                _helperProcess,
                _monitorCts.Token);
            _isCapturing = true;

            var ffmpegInputArgs =
                $"-thread_queue_size 1024 -f s16le -ar 48000 -ac 1 -i \"{fifoPath}\" ";
            return new MicrophoneCaptureInfo(
                fifoPath,
                48000,
                1,
                ffmpegInputArgs);
        }
        catch (Exception ex)
        {
            await CleanupCoreAsync();
            AudioErrorOccurred?.Invoke(this, ex.Message);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopCaptureAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CleanupCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MonitorStderrAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(
                    cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (line.StartsWith("ERROR ", StringComparison.Ordinal))
                {
                    AudioErrorOccurred?.Invoke(this, line[6..]);
                }
            }

            if (!cancellationToken.IsCancellationRequested &&
                process.HasExited &&
                process.ExitCode != 0)
            {
                AudioErrorOccurred?.Invoke(
                    this,
                    $"OpenCam.Microphone exited with code {process.ExitCode}.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                AudioErrorOccurred?.Invoke(this, ex.Message);
            }
        }
    }

    private async Task CleanupCoreAsync()
    {
        _isCapturing = false;
        _monitorCts?.Cancel();

        if (_helperProcess is not null)
        {
            try
            {
                if (!_helperProcess.HasExited)
                {
                    _helperProcess.Kill(entireProcessTree: true);
                }

                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(3));
                await _helperProcess.WaitForExitAsync(timeoutCts.Token);
            }
            catch (Exception ex)
            {
                AudioErrorOccurred?.Invoke(
                    this,
                    $"Unable to stop OpenCam.Microphone cleanly: {ex.Message}");
            }
        }

        if (_stderrMonitorTask is not null)
        {
            try
            {
                await _stderrMonitorTask;
            }
            catch (Exception ex)
            {
                AudioErrorOccurred?.Invoke(
                    this,
                    $"Unable to finish microphone monitoring: {ex.Message}");
            }
        }

        _helperProcess?.Dispose();
        _helperProcess = null;
        _stderrMonitorTask = null;
        _monitorCts?.Dispose();
        _monitorCts = null;

        if (!string.IsNullOrWhiteSpace(_captureDirectory) &&
            Directory.Exists(_captureDirectory))
        {
            try
            {
                Directory.Delete(_captureDirectory, recursive: true);
            }
            catch (Exception ex)
            {
                AudioErrorOccurred?.Invoke(
                    this,
                    $"Unable to remove microphone capture directory: {ex.Message}");
            }
        }
        _captureDirectory = null;
    }

    private static string CreateDefaultTempDirectory() =>
        Path.Combine(
            Path.GetTempPath(),
            "OpenCamMicrophone_" + Guid.NewGuid().ToString("N"));

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await StopCaptureAsync();
        _gate.Dispose();
    }
}
