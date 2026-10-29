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
    private readonly PreflightAudioMeters _meters = new();
    private int _systemVolume = 100, _micVolume = 100;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private Process? _process;
    private Task _stderr = Task.CompletedTask, _copy = Task.CompletedTask;
    private readonly TaskCompletionSource<bool> _ack = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _systemSelected, _micSelected, _readLevels, _started, _disposed;
    private bool _systemStarted, _micStarted, _observerStarted, _observerUnavailable;
    private volatile bool _stopping, _running;
    private string? _error;
    public string? ErrorCode => Volatile.Read(ref _error);
    public bool IsRunning => _running && ErrorCode is null && _process is { HasExited: false };

    public async Task StartAsync(RecordingConfiguration configuration, Stream? trialOutput, CancellationToken ct)
    {
        var startupClock = Stopwatch.StartNew();
        await _lifecycle.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) throw new InvalidOperationException("Audio capture instances are single use.");
            _started = true;
            _systemVolume = configuration.SystemAudioVolumePercent;
            _micVolume = configuration.MicrophoneVolumePercent;
            _systemSelected = configuration.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone;
            _micSelected = configuration.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone;
            if (!_systemSelected && !_micSelected) throw new InvalidOperationException("No audio source selected.");
            try
            {
                SystemAudioCaptureInfo? sys = null;
                MicrophoneCaptureInfo? mic = null;
                using var sources = CancellationTokenSource.CreateLinkedTokenSource(ct);
                async Task StartSystemAsync()
                {
                    if (!_systemSelected || synthetic) return;
                    try
                    {
                        sources.Token.ThrowIfCancellationRequested();
                        if (system?.IsSupported != true) throw new IOException("System audio is unavailable.");
                        system.AudioErrorOccurred += SourceFailed;
                        _systemStarted = true; // even a partially initialized source needs stopping
                        sys = await system.StartCaptureAsync(configuration.MonitorIndex, sources.Token)
                            ?? throw new IOException("System audio did not start.");
                    }
                    catch { sources.Cancel(); throw; }
                }
                async Task StartMicrophoneAsync()
                {
                    if (!_micSelected || synthetic || microphone is null) return;
                    try
                    {
                        sources.Token.ThrowIfCancellationRequested();
                        if (!microphone.IsSupported) throw new IOException("Microphone is unavailable.");
                        microphone.AudioErrorOccurred += SourceFailed;
                        _micStarted = true;
                        mic = await microphone.StartCaptureAsync(configuration.MicrophoneDeviceId, sources.Token)
                            ?? throw new IOException("Microphone did not start.");
                    }
                    catch { sources.Cancel(); throw; }
                }
                // Always join both starts before cleanup: a late start must never race StopCoreAsync.
                await Task.WhenAll(StartSystemAsync(), StartMicrophoneAsync());
                ct.ThrowIfCancellationRequested();
                Log.Information("Audio startup stage: Stage={Stage}, ElapsedMs={ElapsedMs}, Trial={Trial}",
                    "sources_ready", startupClock.Elapsed.TotalMilliseconds, trialOutput is not null);
                if (_micSelected && !synthetic && microphone is null)
                {
                    // FFmpeg owns the selected recording input. This separate
                    // shared-mode tap supplies only the best-effort level meter.
                    _observerUnavailable = true;
                    if (observer is not null && !string.IsNullOrWhiteSpace(configuration.MicrophoneDeviceId))
                    {
                        _observerStarted = true; // also stop a partially initialized tap
                        try { _observerUnavailable = !await observer.StartAsync(configuration.MicrophoneDeviceId, ct); }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception ex) { Log.Warning(ex, "Preflight microphone level monitoring unavailable"); }
                    }
                    ct.ThrowIfCancellationRequested();
                    if (_observerUnavailable) Log.Warning("Preflight microphone meter unavailable; recording input still requires FFmpeg acknowledgement");
                }
                using var sysFd = InheritedAudioDescriptor.Duplicate(sys?.PcmStream);
                using var micFd = InheritedAudioDescriptor.Duplicate(mic?.PcmStream);
                var input = provider.BuildPreflightAudioInputArguments(configuration, synthetic,
                    sysFd?.Input(sys!.Channels) ?? sys?.FfmpegInputArgs,
                    micFd?.Input(mic!.Channels) ?? mic?.FfmpegInputArgs);
                var process = new Process { StartInfo = new(executable ?? FFmpegDiscovery.FindFFmpegExecutable()
                    ?? throw new FileNotFoundException("FFmpeg unavailable."))
                {
                    Arguments = $"-hide_banner -n -stats_period 0.1 -progress pipe:2 -nostats {input} {PreflightAudioFormat.OutputArguments(trialOutput is not null)}",
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
                Log.Information("Audio startup stage: Stage={Stage}, ElapsedMs={ElapsedMs}, Trial={Trial}",
                    "audio_confirmed", startupClock.Elapsed.TotalMilliseconds, trialOutput is not null);
            }
            catch (Exception ex)
            {
                // Capture evidence before cleanup destroys process/task state.
                // Do not include command lines, selected device names or audio.
                Log.Warning(ex, "Preflight audio initialization failed: Microphone={Microphone}, SystemAudio={SystemAudio}, ElapsedMs={ElapsedMs}, Acknowledgement={Acknowledgement}, ProcessStarted={ProcessStarted}, ProcessExited={ProcessExited}, ProgressReader={ProgressReader}, OutputDrain={OutputDrain}",
                    _micSelected, _systemSelected, startupClock.Elapsed.TotalMilliseconds,
                    _ack.Task.Status, _process is not null, _process?.HasExited,
                    _stderr?.Status, _copy?.Status);
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
            void ObserveLine(string line)
            {
                _meters.Observe(line, DateTimeOffset.UtcNow);
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                    long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var time) && time > 0)
                    _ack.TrySetResult(true);
                if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    Log.Debug("Preflight audio FFmpeg: {Line}", line.Length > 2048 ? line[..2048] : line);
            }
            if (OperatingSystem.IsWindows())
                await DedicatedProcessIo.Run(() => {
                    while (process.StandardError.ReadLine() is { } line) ObserveLine(line);
                }).ConfigureAwait(false);
            else
                while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line) ObserveLine(line);
            if (!_stopping)
            {
                Log.Warning("Preflight audio FFmpeg progress ended unexpectedly: Exited={Exited}, ExitCode={ExitCode}, StartupAcknowledged={StartupAcknowledged}",
                    process.HasExited, process.HasExited ? process.ExitCode : (int?)null, _ack.Task.IsCompletedSuccessfully && _ack.Task.Result);
                Volatile.Write(ref _error, "PreflightAudioUnavailable");
            }
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
        try
        {
            if (OperatingSystem.IsWindows())
                await DedicatedProcessIo.CopyAsync(process.StandardOutput.BaseStream, destination).ConfigureAwait(false);
            else
                await process.StandardOutput.BaseStream.CopyToAsync(destination).ConfigureAwait(false);
        }
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
        lock (_levels)
        {
            var measured = _readLevels && IsRunning ? _meters.Read("microphone", now) : null;
            if (_micSelected && _readLevels && IsRunning && _observerUnavailable && measured is null)
                return new(PreflightSourceState.Unavailable);
            var raw = measured ?? (_readLevels && IsRunning ? (microphone as IAudioLevelSource)?.ReadLatestLevel(now) ?? observer?.ReadLatestLevel(now) : null);
            return Attenuate(PreflightReadiness.Resolve(_micSelected, raw, now), _micVolume);
        }
    }
    public PreflightSourceResult ReadSystemAudio(DateTimeOffset now)
    {
        lock (_levels) return Attenuate(PreflightReadiness.Resolve(_systemSelected,
            _readLevels && IsRunning ? _meters.Read("system", now) ?? (system as IAudioLevelSource)?.ReadLatestLevel(now) : null, now), _systemVolume);
    }

    private static PreflightSourceResult Attenuate(PreflightSourceResult source, int percent) =>
        source with { Rms = source.Rms * percent / 100d, Peak = source.Peak * percent / 100d };

    public AudioLevelSample? ReadMix(DateTimeOffset now)
    {
        lock (_levels) return _readLevels && IsRunning ? _meters.Read("mix", now) : null;
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
