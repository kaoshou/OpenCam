// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Threading.Channels;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Infrastructure.Projects;

/// <summary>Nonblocking bounded handoff from the native stderr drain to private disk storage.
/// Overflow or producer errors invalidate the sidecar, never stall/stop the video producer.</summary>
public sealed class PointerTrackCollector : IAsyncDisposable
{
    private readonly PointerTrackWriter _writer;
    private readonly Channel<NativePointerEvent> _queue = Channel.CreateBounded<NativePointerEvent>(
        new BoundedChannelOptions(2048) { SingleReader=true, FullMode=BoundedChannelFullMode.Wait });
    private readonly Task _pump;
    private int _failed, _closed;
    private bool _disposed;

    public PointerTrackCollector(PointerTrackWriter writer)
    { _writer=writer; _pump=Task.Run(DrainAsync); }

    public bool TryAppend(NativePointerEvent item)
    {
        if (Volatile.Read(ref _closed)!=0 || Volatile.Read(ref _failed)!=0) return false;
        if (_queue.Writer.TryWrite(item)) return true;
        MarkIncomplete(); return false;
    }
    public void MarkIncomplete()=>Interlocked.Exchange(ref _failed,1);

    private async Task DrainAsync()
    {
        await foreach(var item in _queue.Reader.ReadAllAsync())
        {
            if(Volatile.Read(ref _failed)!=0) continue;
            try { _writer.Append(item); }
            catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
            { MarkIncomplete(); }
        }
    }

    public async Task<bool> CompleteAsync(PointerTrackSource source)
    {
        if(Interlocked.Exchange(ref _closed,1)!=0) return false;
        _queue.Writer.TryComplete(); await _pump;
        if(Volatile.Read(ref _failed)!=0) return false;
        try { _writer.Seal(source); return true; }
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        { MarkIncomplete(); return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if(_disposed) return;
        Interlocked.Exchange(ref _closed,1); _queue.Writer.TryComplete();
        try { await _pump; } finally { _writer.Dispose(); _disposed=true; }
    }
}
