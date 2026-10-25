// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Media.Capture;

/// <summary>Selects a core once, before capture starts; source loss and startup failure never switch it silently.</summary>
public sealed class ObsRecordingEngineRouter : IScreenRecorderEngine, IBoundRecordingOutput,
    IRecordingAudioLevelSource, INativePointerCapture
{
    private readonly ObsRuntime? _runtime;
    private readonly Func<IScreenRecorderEngine> _createLegacy;
    private readonly Func<RecordingConfiguration, CaptureRegion, CaptureSelection?, MonitorInfo?> _resolveMonitor;
    private readonly HardwareEncoderType _encoder;
    private readonly CaptureSelection? _pinnedCapture;
    private readonly bool _requireNative;
    private IScreenRecorderEngine? _inner;
    private CaptureRegion? _bounds;
    private bool _legacy;
    private bool _disposed;

    public ObsRecordingEngineRouter(ObsRuntime? runtime, Func<IScreenRecorderEngine> createLegacy,
        Func<RecordingConfiguration, CaptureRegion, CaptureSelection?, MonitorInfo?> resolveMonitor,
        HardwareEncoderType encoder, CaptureSelection? pinnedCapture = null, bool requireNative = false)
    {
        _runtime = runtime;
        _createLegacy = createLegacy;
        _resolveMonitor = resolveMonitor;
        _encoder = encoder;
        _pinnedCapture = pinnedCapture;
        _requireNative = requireNative;
    }

    public Stream? OutputStream { get; set; }
    public bool UsesDirectPreview => (_inner as IBoundRecordingOutput)?.UsesDirectPreview ?? false;
    public bool UseSyntheticCaptureSource { get; set; }
    public bool IsRunning => _inner?.IsRunning ?? false;
    public TimeSpan CurrentRecordedTime => _inner?.CurrentRecordedTime ?? TimeSpan.Zero;
    public long CurrentFramesRecorded => _inner?.CurrentFramesRecorded ?? 0;
    public HardwareEncoderType ActiveEncoder => _inner?.ActiveEncoder ?? _encoder;
    public CaptureSelection? ActiveCapture => _legacy && _bounds is not null
        ? (_inner?.ActiveCapture ?? _pinnedCapture ?? new(CaptureBackend.PlatformDefault, CaptureFallbackReason.None, null, null, null, _bounds))
            with { RecordingCore = "ffmpeg" }
        : _inner?.ActiveCapture;
    public event EventHandler<string>? EngineErrorOccurred;
    public event EventHandler<string>? EngineWarningOccurred;
    public event EventHandler? AudioDeviceLost;
    public event EventHandler<NativePointerEvent>? PointerMetadata;
    public event EventHandler<string>? PointerIncomplete;

    public async Task StartRecordingAsync(string workingFilePath, RecordingConfiguration config, CaptureRegion actualBounds,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inner is not null) throw new InvalidOperationException("Create a fresh recording engine for each segment.");
        cancellationToken.ThrowIfCancellationRequested();
        _bounds = actualBounds;
        var pinnedObs = _pinnedCapture?.RecordingCore == "libobs-v1";
        if (_pinnedCapture?.RecordingCore is not (null or "ffmpeg" or "libobs-v1"))
            throw new IOException("The recording session requires an unsupported pinned recording core.");
        // Old CaptureSelection values predate RecordingCore and describe the existing FFmpeg path.
        var pinnedLegacy = _pinnedCapture is not null && !pinnedObs;
        var validationClock = System.Diagnostics.Stopwatch.StartNew();
        var currentRuntime = !pinnedLegacy && !UseSyntheticCaptureSource && _runtime is not null
            ? ObsRuntime.TryLoad(_runtime.Root) : null;
        Serilog.Log.Information("Recording startup stage: Stage={Stage}, ElapsedMs={ElapsedMs}, Available={Available}",
            "runtime_start_validation", validationClock.Elapsed.TotalMilliseconds, currentRuntime is not null);
        MonitorInfo? monitor = null;
        if (currentRuntime is not null && !config.EditablePointerEnabled && SupportsCaptureMode(config, currentRuntime.Platform))
        {
            monitor = _resolveMonitor(config, actualBounds, _pinnedCapture);
            if (monitor is not null)
            {
                try
                {
                    var request = ObsCaptureRequest.Create(config, actualBounds, monitor, _encoder, false);
                    _ = request.Serialize();
                    var backend = currentRuntime.Platform == "win-x64" ? "wgc" : "sck";
                    if (!currentRuntime.Backends.Contains(backend) || !currentRuntime.AudioModes.Contains(request.Audio) ||
                        !currentRuntime.Encoders.Contains(request.Encoder)) monitor = null;
                }
                catch (ArgumentException) { monitor = null; }
            }
        }
        if (pinnedObs && (currentRuntime is null || monitor is null || UseSyntheticCaptureSource || config.EditablePointerEnabled))
            throw new IOException("The pinned recording runtime or source is unavailable; the session cannot switch recording cores.");
        var legacy = currentRuntime is null || monitor is null || pinnedLegacy || UseSyntheticCaptureSource || config.EditablePointerEnabled;
        if (_requireNative && legacy)
            throw new IOException("The recording core or selected source is unavailable. Recording did not start; check the installation, selected display and audio devices.");
        _legacy = legacy;
        _inner = _legacy ? _createLegacy() : new ObsScreenRecorderEngine(currentRuntime!.HelperPath, currentRuntime.Root,
            (_, _, _) => monitor, _encoder, _pinnedCapture, allowCompatibilityFallback: !_requireNative);
        _inner.UseSyntheticCaptureSource = UseSyntheticCaptureSource;
        if (_inner is IBoundRecordingOutput bound) bound.OutputStream = OutputStream;
        else if (OutputStream is not null) throw new InvalidOperationException("Recording core cannot accept owned output.");
        _inner.EngineErrorOccurred += OnError;
        _inner.EngineWarningOccurred += OnWarning;
        _inner.AudioDeviceLost += OnAudioLost;
        if (_inner is INativePointerCapture pointer)
        {
            pointer.PointerMetadata += OnPointer;
            pointer.PointerIncomplete += OnPointerIncomplete;
        }
        await _inner.StartRecordingAsync(workingFilePath, config, actualBounds, cancellationToken).ConfigureAwait(false);
    }

    public Task StopRecordingAsync(CancellationToken cancellationToken = default) =>
        _inner?.StopRecordingAsync(cancellationToken) ?? Task.CompletedTask;

    public (AudioLevelSample? SystemAudio, AudioLevelSample? Microphone) ReadInputLevels(DateTimeOffset now) =>
        (_inner as IRecordingAudioLevelSource)?.ReadInputLevels(now) ?? (null, null);

    private void OnError(object? sender, string error) => EngineErrorOccurred?.Invoke(this, error);
    private void OnWarning(object? sender, string warning) => EngineWarningOccurred?.Invoke(this, warning);
    private void OnAudioLost(object? sender, EventArgs args) => AudioDeviceLost?.Invoke(this, args);
    private void OnPointer(object? sender, NativePointerEvent pointer) => PointerMetadata?.Invoke(this, pointer);
    private void OnPointerIncomplete(object? sender, string reason) => PointerIncomplete?.Invoke(this, reason);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_inner is null) return;
        _inner.EngineErrorOccurred -= OnError;
        _inner.EngineWarningOccurred -= OnWarning;
        _inner.AudioDeviceLost -= OnAudioLost;
        if (_inner is INativePointerCapture pointer)
        {
            pointer.PointerMetadata -= OnPointer;
            pointer.PointerIncomplete -= OnPointerIncomplete;
        }
        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    public static MonitorInfo? ResolveMonitor(RecordingConfiguration config, CaptureRegion bounds, CaptureSelection? pinned,
        IReadOnlyList<MonitorInfo> monitors, Func<MonitorInfo, string?> nativeIdentity)
    {
        var containing = monitors.Where(monitor => bounds.X >= monitor.Bounds.X && bounds.Y >= monitor.Bounds.Y &&
            (long)bounds.X + bounds.Width <= (long)monitor.Bounds.X + monitor.Bounds.Width &&
            (long)bounds.Y + bounds.Height <= (long)monitor.Bounds.Y + monitor.Bounds.Height).ToArray();
        if (containing.Length != 1) return null;
        var target = containing[0];
        if (pinned is null && config.CaptureSource == CaptureSourceType.Monitor && target.Index != config.MonitorIndex) return null;
        var identity = nativeIdentity(target);
        if (string.IsNullOrWhiteSpace(identity)) return null;
        if (pinned is not null && (pinned.RecordingCore != "libobs-v1" || identity != pinned.DeviceName ||
            bounds != pinned.Bounds || target.Bounds != pinned.OutputBounds)) return null;
        return target with { DeviceName = identity };
    }

    internal static bool SupportsCaptureMode(RecordingConfiguration config, string platform) =>
        // Manual Windows and macOS standard choices retain their existing implementations.
        platform switch
        {
            "win-x64" => config.WindowsCaptureMode == WindowsCaptureMode.Automatic,
            "osx-arm64" => config.NativeCaptureEnabled,
            _ => false
        };
}
