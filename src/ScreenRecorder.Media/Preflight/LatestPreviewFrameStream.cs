// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Media.Preflight;

/// <summary>Single producer, snapshot readers. Incomplete/older pictures are never queued for the UI.</summary>
public sealed class LatestPreviewFrameStream : Stream
{
    private byte[]? _partial = new byte[LivePreviewFormat.FrameBytes], _latest;
    private int _used;
    private readonly object _gate = new();
    public byte[]? LatestFrame => Volatile.Read(ref _latest);
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => _partial is not null;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_partial is null, this);
            while (!bytes.IsEmpty)
            {
                var size = Math.Min(bytes.Length, _partial.Length - _used);
                bytes[..size].CopyTo(_partial.AsSpan(_used));
                bytes = bytes[size..]; _used += size;
                if (_used != _partial.Length) continue;
                // Never mutate a published array: an in-flight IPC reader owns its snapshot.
                Volatile.Write(ref _latest, _partial);
                _partial = new byte[LivePreviewFormat.FrameBytes];
                _used = 0;
            }
        }
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); Write(buffer.Span); return ValueTask.CompletedTask; }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); Write(buffer, offset, count); return Task.CompletedTask; }
    protected override void Dispose(bool disposing)
    {
        lock (_gate) { _partial = null; Volatile.Write(ref _latest, null); _used = 0; }
        base.Dispose(disposing);
    }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
