// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;

namespace ScreenRecorder.Media.Projects;

/// <summary>Verified source pointer lookup with two retained points. Forward playback
/// streams the owned file; backward seeks reopen and verify it asynchronously.</summary>
public sealed class PointerTrackCursor : IDisposable
{
    private readonly IProjectHandle _owner;
    private readonly ProjectSource _source;
    private readonly CaptureClockAnchor _anchor;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly object _lifecycle = new();
    private readonly PointerSample[] _window = new PointerSample[2];
    private PointerTrackReader? _reader;
    private PointerSample? _before, _after;
    private long? _lastHost;
    private bool _primed, _disposed;

    private PointerTrackCursor(IProjectHandle owner, ProjectSource source,
        CaptureClockAnchor anchor, PointerTrackReader reader)
    { _owner = owner; _source = source; _anchor = anchor; _reader = reader; }

    public static async Task<PointerTrackCursor> OpenAsync(IProjectHandle owner, ProjectSource source,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);
        var timeBase = source.Timing?.TimeBase;
        if (timeBase is null || timeBase.Numerator <= 0 || timeBase.Denominator <= 0)
            throw new InvalidDataException("Invalid pointer source time base.");
        NativeFrameClock? first = null;
        using (var validation = await PointerTrackReader.OpenVerifiedAsync(owner, source, ct).ConfigureAwait(false))
        {
            while (validation.ReadNext() is { } item)
            {
                ct.ThrowIfCancellationRequested();
                first ??= item.Frame;
            }
            if (!validation.Complete || first is null)
                throw new InvalidDataException("Pointer track has no complete verified clock.");
        }
        // This fresh reader again checks the immutable manifest fingerprint, so
        // the streaming pass cannot silently select another file after validation.
        var reader = await PointerTrackReader.OpenVerifiedAsync(owner, source, ct).ConfigureAwait(false);
        return new(owner, source, new(first.Value.SourcePtsMicroseconds, first.Value.PresentationMicroseconds), reader);
    }

    public async ValueTask<PointerSample?> ResolveAsync(long sourcePts, CancellationToken ct = default)
    {
        await _serial.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            PointerTrackReader reader;
            lock (_lifecycle)
            { ObjectDisposedException.ThrowIf(_disposed, this); reader = _reader!; }
            var timeBase = _source.Timing.TimeBase;
            var nativeUs = (BigInteger)sourcePts * timeBase.Numerator * 1_000_000 / timeBase.Denominator
                - _source.Pointer!.VideoPtsOffsetMicroseconds;
            var target = nativeUs + _anchor.HostMicroseconds - _anchor.SourcePtsMicroseconds;
            if (target < long.MinValue || target > long.MaxValue)
                throw new InvalidDataException("Pointer source time exceeds supported limits.");
            var host = (long)target;
            if (_lastHost is { } previous && host < previous)
            {
                var replacement = await PointerTrackReader.OpenVerifiedAsync(_owner, _source, ct).ConfigureAwait(false);
                lock (_lifecycle)
                {
                    if (_disposed) { replacement.Dispose(); throw new ObjectDisposedException(nameof(PointerTrackCursor)); }
                    _reader!.Dispose(); _reader = replacement; reader = replacement;
                    _before = null; _after = null; _primed = false;
                }
            }
            if (!_primed) { _after = ReadPointer(reader, ct); _primed = true; }
            while (_after is { } next && next.HostMicroseconds <= host)
            {
                _before = next;
                _after = ReadPointer(reader, ct);
            }
            _lastHost = host;
            if (_before is not { } before) return null;
            if (before.HostMicroseconds == host) return before.Visible ? before : null;
            if (_after is not { } after) return null;
            _window[0] = before; _window[1] = after;
            return PointerTimeline.Resolve(_window, host);
        }
        finally { _serial.Release(); }
    }

    private static PointerSample? ReadPointer(PointerTrackReader reader, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var item = reader.ReadNext();
            if (item is null)
            {
                if (!reader.Complete) throw new InvalidDataException("Pointer track ended before its verified seal.");
                return null;
            }
            if (item.Value.Pointer is { } point) return point;
        }
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true; _reader?.Dispose(); _reader = null;
        }
    }
}
