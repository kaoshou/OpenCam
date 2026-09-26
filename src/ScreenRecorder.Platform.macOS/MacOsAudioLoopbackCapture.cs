// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Core.Audio;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.macOS;

public sealed class MacOsAudioLoopbackCapture : ISystemAudioLoopbackCapture, IAudioLevelSource
{
    private const string ReadyLine =
        "READY sample-rate=48000 channels=2 format=s16le";

    private readonly string _helperPath;
    private readonly Func<int, uint?> _resolveDisplayId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AudioLevelAccumulator _levels = new();

    private Process? _helperProcess;
    private CancellationTokenSource? _monitorCts;
    private Task? _stderrMonitorTask;
    private bool _isCapturing;
    private bool _isDisposed;

    public MacOsAudioLoopbackCapture()
        : this(
            MacOsSystemAudioSupport.HelperPath(AppContext.BaseDirectory),
            monitorIndex =>
                new MacOsDisplayService().GetNativeDisplayId(monitorIndex))
    {
    }

    internal MacOsAudioLoopbackCapture(
        string helperPath,
        Func<int, uint?> resolveDisplayId)
    {
        _helperPath = helperPath;
        _resolveDisplayId = resolveDisplayId;
    }

    public bool IsSupported =>
        OperatingSystem.IsMacOS() &&
        MacOsSystemAudioSupport.IsSupported(
            Environment.OSVersion.Version,
            Path.GetDirectoryName(_helperPath) ?? AppContext.BaseDirectory);

    public bool IsCapturing => _isCapturing;

    public AudioLevelSample? ReadLatestLevel(DateTimeOffset now) =>
        _isCapturing ? _levels.ReadFresh(now, TimeSpan.FromSeconds(1)) : null;

    public event EventHandler<string>? AudioErrorOccurred;

    public async Task<SystemAudioCaptureInfo?> StartCaptureAsync(
        int monitorIndex,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_isCapturing)
            {
                throw new InvalidOperationException(
                    "macOS system audio capture is already running.");
            }

            if (!OperatingSystem.IsMacOS() || !File.Exists(_helperPath))
            {
                return null;
            }

            var displayId = _resolveDisplayId(monitorIndex);
            if (!displayId.HasValue)
            {
                throw new InvalidOperationException(
                    "No active macOS display is available for system audio capture.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _helperPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--stdout");
            startInfo.ArgumentList.Add("--display-id");
            startInfo.ArgumentList.Add(displayId.Value.ToString());

            _helperProcess = Process.Start(startInfo) ??
                throw new InvalidOperationException(
                    "Unable to launch OpenCam.SystemAudio.");

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
                        "OpenCam.SystemAudio exited before reporting readiness.");
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
                $"-thread_queue_size 1024 -f s16le -ar 48000 -ac 2 -i \"pipe:0\" ";
            return new SystemAudioCaptureInfo(
                string.Empty,
                48000,
                2,
                ffmpegInputArgs) { PcmStream = _helperProcess.StandardOutput.BaseStream };
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
                else if (MacOsLevelLineParser.TryParse(line, out var rms, out var peak))
                {
                    _levels.PublishNormalized(rms, peak, DateTimeOffset.UtcNow);
                }
            }

            if (!cancellationToken.IsCancellationRequested &&
                process.HasExited &&
                process.ExitCode != 0)
            {
                AudioErrorOccurred?.Invoke(
                    this,
                    $"OpenCam.SystemAudio exited with code {process.ExitCode}.");
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
        _levels.Clear();
        try { _monitorCts?.Cancel(); } catch { }

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
            catch
            {
            }
        }

        if (_stderrMonitorTask is not null)
        {
            try { await _stderrMonitorTask; } catch { }
        }

        _helperProcess?.Dispose();
        _helperProcess = null;
        _stderrMonitorTask = null;
        _monitorCts?.Dispose();
        _monitorCts = null;


    }


    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await StopCaptureAsync();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
