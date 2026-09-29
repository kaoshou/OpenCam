// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.FFmpeg;
using Serilog;

namespace ScreenRecorder.Media.Encoders;

/// <summary>One policy per process; recorder startup remains authoritative.</summary>
public class FFmpegEncoderDetector : IEncoderSelectionService
{
    private readonly string _ffmpegPath;
    private readonly IFFmpegPlatformProvider _platformProvider;
    private readonly IEncoderProbeRunner _runner;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly object _cacheLock = new();
    private readonly Dictionary<HardwareEncoderType, CacheEntry> _cache = new();
    private ExecutableStamp? _stamp;
    private string? _version;
    private DateTimeOffset _versionValidUntil;
    private long _revision;

    private sealed record ExecutableStamp(string Path, long Length, DateTime LastWriteUtc);
    private sealed record CacheEntry(EncoderProbeResult Result, DateTimeOffset ValidUntil);

    public FFmpegEncoderDetector(IFFmpegPlatformProvider platformProvider, string? ffmpegPath = null,
        IEncoderProbeRunner? probeRunner = null, TimeProvider? timeProvider = null)
    {
        _platformProvider = platformProvider ?? throw new ArgumentNullException(nameof(platformProvider));
        _ffmpegPath = Path.GetFullPath(ffmpegPath ?? FFmpegDiscovery.FindFFmpegExecutable()
            ?? throw new FileNotFoundException("未在系統中探測到 FFmpeg 執行檔"));
        _runner = probeRunner ?? new EncoderProbeRunner();
        _clock = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<EncoderCapability>> DetectAvailableEncodersAsync(CancellationToken cancellationToken = default) =>
        DetectAsync(HardwareEncoderType.Auto, false, cancellationToken);

    public async Task<HardwareEncoderType> ResolveOptimalEncoderAsync(HardwareEncoderType preferred,
        CancellationToken cancellationToken = default) => (await SelectAsync(preferred, cancellationToken)).Encoder;

    public async Task<EncoderSelection> SelectAsync(HardwareEncoderType preferred, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (preferred == HardwareEncoderType.SoftwareCpu)
            return PublishSelection(preferred, EncoderFallbackReason.None);
        var capabilities = await DetectAsync(preferred, true, cancellationToken).ConfigureAwait(false);
        var available = capabilities.FirstOrDefault(c => c.IsAvailable &&
            c.Type is not HardwareEncoderType.Auto and not HardwareEncoderType.SoftwareCpu);
        return available != null
            ? PublishSelection(available.Type, EncoderFallbackReason.None)
            : PublishSelection(HardwareEncoderType.SoftwareCpu, preferred == HardwareEncoderType.Auto
                ? EncoderFallbackReason.NoValidatedHardware : EncoderFallbackReason.RequestedHardwareUnavailable);
    }

    public void ReportStartupFailure(HardwareEncoderType encoder)
    {
        if (encoder is HardwareEncoderType.Auto or HardwareEncoderType.SoftwareCpu) return;
        lock (_cacheLock)
        {
            _revision++;
            _cache[encoder] = new(new(EncoderProbeStatus.ProbeFailed, null, TimeSpan.Zero, "", "Formal startup failed", true),
                _clock.GetUtcNow() + EncoderProbePolicy.FailureCacheLifetime);
        }
        Log.Warning("Encoder {Encoder} formal startup failed; probe cache invalidated for {CooldownSeconds}s",
            encoder, EncoderProbePolicy.FailureCacheLifetime.TotalSeconds);
    }

    private async Task<IReadOnlyList<EncoderCapability>> DetectAsync(HardwareEncoderType preferred,
        bool stopOnFirst, CancellationToken token)
    {
        var started = _clock.GetTimestamp();
        var probes = _platformProvider.GetHardwareEncoderProbes()
            .Where(p => preferred == HardwareEncoderType.Auto || p.Type == preferred).ToArray();
        var result = new List<EncoderCapability>
        {
            new(HardwareEncoderType.Auto, "auto", "自動選擇 (優先硬體加速)", true),
            new(HardwareEncoderType.SoftwareCpu, "libx264", "CPU 軟體編碼 (libx264 相容穩定)", true)
        };
        result.AddRange(probes.Select(p => new EncoderCapability(p.Type, p.EncoderName, p.EncoderName, false)));
        if (!await _semaphore.WaitAsync(EncoderProbePolicy.DetectionBudget, token).ConfigureAwait(false))
        {
            Log.Warning("Encoder detection budget exhausted while waiting for another probe");
            return result;
        }
        try
        {
            token.ThrowIfCancellationRequested();
            if (!await EnsureIdentityAsync(started, token).ConfigureAwait(false)) return result;
            for (var i = 0; i < probes.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                if (Remaining(started) <= TimeSpan.Zero) break;
                var probe = probes[i];
                EncoderProbeResult? outcome;
                long revision;
                lock (_cacheLock)
                {
                    revision = _revision;
                    outcome = _cache.TryGetValue(probe.Type, out var cached) && cached.ValidUntil > _clock.GetUtcNow()
                        ? cached.Result : null;
                }
                Log.Debug("Encoder {Encoder} probe cache hit={CacheHit}", probe.Type, outcome != null);
                if (outcome == null)
                {
                    outcome = await RunAsync($"-nostdin -f lavfi -i testsrc=size=256x256:rate=30 -t 0.05 -c:v {probe.EncoderName} {probe.ExtraArgs} -f null -",
                        started, token).ConfigureAwait(false);
                    if (outcome == null) break;
                    Log.Information("Encoder probe {Encoder}: {ProbeStatus}, exit={ExitCode}, {ElapsedMs}ms; stderr={StderrTail}",
                        probe.Type, outcome.Status, outcome.ExitCode, outcome.Elapsed.TotalMilliseconds,
                        Sanitize(outcome.StderrTail, EncoderProbePolicy.TailCharacterLimit));
                    EnsureUsableResult(outcome, token);
                    lock (_cacheLock)
                    {
                        if (_revision == revision)
                            _cache[probe.Type] = new(outcome, _clock.GetUtcNow() +
                                (outcome.Status == EncoderProbeStatus.Available ? EncoderProbePolicy.SuccessCacheLifetime : EncoderProbePolicy.FailureCacheLifetime));
                        else if (_cache.TryGetValue(probe.Type, out var newer) && newer.ValidUntil > _clock.GetUtcNow())
                            outcome = newer.Result;
                    }
                }
                var available = outcome.Status == EncoderProbeStatus.Available;
                result[i + 2] = result[i + 2] with { IsAvailable = available };
                if (stopOnFirst && available) break;
            }
            return result;
        }
        finally { _semaphore.Release(); }
    }

    private async Task<bool> EnsureIdentityAsync(long started, CancellationToken token)
    {
        ExecutableStamp stamp;
        try
        {
            var file = new FileInfo(_ffmpegPath);
            stamp = new(_ffmpegPath, file.Length, file.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_cacheLock) { _cache.Clear(); _stamp = null; _version = null; _revision++; }
            Log.Warning("FFmpeg identity unavailable: {FailureType}", ex.GetType().Name);
            return false;
        }
        lock (_cacheLock)
        {
            if (stamp != _stamp)
            {
                _stamp = stamp;
                _cache.Clear();
                _version = null;
                _versionValidUntil = DateTimeOffset.MinValue;
                _revision++;
            }
            if (_versionValidUntil > _clock.GetUtcNow()) return _version != null;
        }
        var outcome = await RunAsync("-version", started, token).ConfigureAwait(false);
        if (outcome == null) return false;
        EnsureUsableResult(outcome, token);
        var version = outcome.Status == EncoderProbeStatus.Available
            ? Sanitize(outcome.StdoutTail.Split('\n')[0], 256) : null;
        if (string.IsNullOrWhiteSpace(version)) version = null;
        lock (_cacheLock)
        {
            if (_version != version) { _cache.Clear(); _revision++; }
            _version = version;
            _versionValidUntil = _clock.GetUtcNow() + (version != null
                ? EncoderProbePolicy.SuccessCacheLifetime : EncoderProbePolicy.FailureCacheLifetime);
        }
        Log.Information("FFmpeg probe identity: {FFmpegPath}, {FFmpegVersion}, status={VersionStatus}",
            _ffmpegPath, version, outcome.Status);
        return version != null;
    }

    private TimeSpan Remaining(long started) => EncoderProbePolicy.DetectionBudget - _clock.GetElapsedTime(started);

    private async Task<EncoderProbeResult?> RunAsync(string arguments, long started, CancellationToken token)
    {
        var remaining = Remaining(started);
        if (remaining <= TimeSpan.Zero) return null;
        return await _runner.RunAsync(new ProcessStartInfo
        {
            FileName = _ffmpegPath, Arguments = arguments, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        }, remaining < EncoderProbePolicy.ProbeTimeout ? remaining : EncoderProbePolicy.ProbeTimeout, token).ConfigureAwait(false);
    }

    private static void EnsureUsableResult(EncoderProbeResult outcome, CancellationToken token)
    {
        if (!outcome.CleanupCompleted)
            throw new InvalidOperationException("Encoder probe cleanup did not complete; recording startup was stopped.");
        token.ThrowIfCancellationRequested();
        if (outcome.Status == EncoderProbeStatus.Cancelled) throw new OperationCanceledException(token);
    }

    private static string Sanitize(string text, int limit)
    {
        if (text.Length > limit) text = text[^limit..];
        return new string(text.Where(c => !char.IsControl(c)).ToArray());
    }

    private static EncoderSelection PublishSelection(HardwareEncoderType encoder, EncoderFallbackReason reason)
    {
        Log.Information("Encoder selected: {Encoder}; fallback={FallbackReason}", encoder, reason);
        return new(encoder, reason);
    }
}
