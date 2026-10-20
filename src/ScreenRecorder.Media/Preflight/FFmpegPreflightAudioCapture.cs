// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.FFmpeg;
using Serilog;

namespace ScreenRecorder.Media.Preflight;

/// <summary>One audio-only operation. Owns its sources; never owns the caller's output stream.</summary>
public sealed class FFmpegPreflightAudioCapture(
    IPreflightAudioPlatformProvider provider,
    ISystemAudioLoopbackCapture? system = null,
    IMicrophoneCapture? microphone = null,
    IMicrophoneLevelObserver? observer = null,
    string? executable = null,
    bool synthetic = false) : IPreflightAudioCapture
{
    private readonly object _levels = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private Process? _process;
    private Task _stderr = Task.CompletedTask, _copy = Task.CompletedTask;
    private readonly TaskCompletionSource<bool> _ack = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _systemSelected, _micSelected, _readLevels, _started, _disposed;
    private bool _systemStarted, _micStarted, _observerStarted;
    private volatile bool _stopping, _running;
    private string? _error;
    public string? ErrorCode => Volatile.Read(ref _error);
    public bool IsRunning => _running && ErrorCode is null && _process is { HasExited: false };

    public async Task StartAsync(RecordingConfiguration configuration, Stream? trialOutput, CancellationToken ct)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) throw new InvalidOperationException("Audio capture instances are single use.");
            _started = true;
            _systemSelected = configuration.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone;
            _micSelected = configuration.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone;
            if (!_systemSelected && !_micSelected) throw new InvalidOperationException("No audio source selected.");
            try
            {
                SystemAudioCaptureInfo? sys = null;
                MicrophoneCaptureInfo? mic = null;
                if (_systemSelected && !synthetic)
                {
                    if (system?.IsSupported != true) throw new IOException("System audio is unavailable.");
                    system.AudioErrorOccurred += SourceFailed;
                    _systemStarted = true; // even a partially initialized source needs stopping
                    sys = await system.StartCaptureAsync(configuration.MonitorIndex, ct)
                        ?? throw new IOException("System audio did not start.");
                }
                if (_micSelected && !synthetic && microphone is not null)
                {
                    if (!microphone.IsSupported) throw new IOException("Microphone is unavailable.");
                    microphone.AudioErrorOccurred += SourceFailed;
                    _micStarted = true;
                    mic = await microphone.StartCaptureAsync(configuration.MicrophoneDeviceId, ct)
                        ?? throw new IOException("Microphone did not start.");
                }
                if (_micSelected && !synthetic && microphone is null)
                {
                    if (observer is null || string.IsNullOrWhiteSpace(configuration.MicrophoneDeviceId))
                        throw new IOException("Microphone monitoring is unavailable.");
                    _observerStarted = true;
                    if (!await observer.StartAsync(configuration.MicrophoneDeviceId, ct))
                        throw new IOException("Microphone monitoring did not start.");
                }
                using var sysFd = InheritedAudioDescriptor.Duplicate(sys?.PcmStream);
                using var micFd = InheritedAudioDescriptor.Duplicate(mic?.PcmStream);
                var input = provider.BuildPreflightAudioInputArguments(configuration, synthetic,
                    sysFd?.Input(sys!.Channels) ?? sys?.FfmpegInputArgs,
                    micFd?.Input(mic!.Channels) ?? mic?.FfmpegInputArgs);
                var process = new Process { StartInfo = new(executable ?? FFmpegDiscovery.FindFFmpegExecutable()
                    ?? throw new FileNotFoundException("FFmpeg unavailable."))
                {
                    Arguments = $"-hide_banner -n -progress pipe:2 -nostats {input} {PreflightAudioFormat.OutputArguments(trialOutput is not null)}",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true
                }};
                try { process.Start(); }
                catch { process.Dispose(); throw; }
                _process = process;
                sysFd?.Dispose(); micFd?.Dispose();
                _stderr = ReadProgressAsync(process);
                _copy = DrainAsync(process, trialOutput ?? Stream.Null);
                var exited = process.WaitForExitAsync();
                using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
                startup.CancelAfter(OperatingSystem.IsMacOS() ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(5));
                await Task.WhenAny(_ack.Task, exited).WaitAsync(startup.Token);
                ct.ThrowIfCancellationRequested();
                if (process.HasExited || !_ack.Task.IsCompletedSuccessfully || !await _ack.Task || ErrorCode is not null)
                    throw new IOException("Audio capture did not acknowledge startup.");
                lock (_levels) { _readLevels = true; _running = true; }
            }
            catch
            {
                Volatile.Write(ref _error, "PreflightAudioUnavailable");
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await StopCoreAsync(cleanup.Token);
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    private void SourceFailed(object? sender, string error)
    {
        if (_stopping) return;
        Log.Warning("Preflight audio source failed: {Error}", error);
        Volatile.Write(ref _error, "PreflightAudioUnavailable");
        _ack.TrySetResult(false);
    }

    private async Task ReadProgressAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                    long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var time) && time > 0)
                    _ack.TrySetResult(true);
                if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    Log.Debug("Preflight audio FFmpeg: {Line}", line.Length > 2048 ? line[..2048] : line);
            }
            if (!_stopping) Volatile.Write(ref _error, "PreflightAudioUnavailable");
            _ack.TrySetResult(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cannot read preflight audio progress");
            if (!_stopping) Volatile.Write(ref _error, "PreflightAudioUnavailable");
            _ack.TrySetResult(false);
        }
    }

    private async Task DrainAsync(Process process, Stream destination)
    {
        try { await process.StandardOutput.BaseStream.CopyToAsync(destination).ConfigureAwait(false); }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cannot write preflight audio output");
            Volatile.Write(ref _error, "PreflightTrialFailed");
            _ack.TrySetResult(false);
            throw;
        }
    }

    public PreflightSourceResult ReadMicrophone(DateTimeOffset now)
    {
        lock (_levels) return PreflightReadiness.Resolve(_micSelected,
            _readLevels && IsRunning ? (microphone as IAudioLevelSource)?.ReadLatestLevel(now) ?? observer?.ReadLatestLevel(now) : null, now);
    }
    public PreflightSourceResult ReadSystemAudio(DateTimeOffset now)
    {
        lock (_levels) return PreflightReadiness.Resolve(_systemSelected,
            _readLevels && IsRunning ? (system as IAudioLevelSource)?.ReadLatestLevel(now) : null, now);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _lifecycle.WaitAsync(ct);
        try { await StopCoreAsync(ct); }
        finally { _lifecycle.Release(); }
    }
    private async Task StopCoreAsync(CancellationToken ct)
    {
        lock (_levels) { _readLevels = false; _running = false; }
        _stopping = true;
        var errors = new List<Exception>();
        // Stop every initialized producer even if a sibling fails to stop.
        if (_observerStarted)
            try { await observer!.StopAsync(ct); _observerStarted = false; } catch (Exception ex) { errors.Add(ex); }
        if (_systemStarted)
            try { await system!.StopCaptureAsync(ct); _systemStarted = false; } catch (Exception ex) { errors.Add(ex); }
        if (_micStarted)
            try { await microphone!.StopCaptureAsync(ct); _micStarted = false; } catch (Exception ex) { errors.Add(ex); }
        if (_process is { } process)
        {
            try
            {
                if (!process.HasExited)
                {
                    try
                    {
                        await process.StandardInput.WriteLineAsync("q".AsMemory(), ct);
                        await process.StandardInput.FlushAsync(ct);
                        await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(6), ct);
                    }
                    catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
                    {
                        Log.Warning(ex, "Joining preflight audio after graceful shutdown failed");
                        if (!process.HasExited) process.Kill(true);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                        // A killed recording cannot be accepted as a sealed trial.
                        Volatile.Write(ref _error, "PreflightTrialFailed");
                    }
                }
                await Task.WhenAll(_stderr, _copy).WaitAsync(TimeSpan.FromSeconds(3));
                if (process.ExitCode != 0) Volatile.Write(ref _error, "PreflightTrialFailed");
                process.Dispose(); _process = null;
            }
            catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new AggregateException("Audio shutdown was not confirmed.", errors);
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StopAsync(deadline.Token);
        if (system is not null) { system.AudioErrorOccurred -= SourceFailed; await system.DisposeAsync(); }
        if (microphone is not null) { microphone.AudioErrorOccurred -= SourceFailed; await microphone.DisposeAsync(); }
        if (observer is not null) await observer.DisposeAsync();
        _disposed = true;
    }
}
