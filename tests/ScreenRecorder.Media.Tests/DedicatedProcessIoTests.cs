// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Pipes;
using ScreenRecorder.Media.FFmpeg;

namespace ScreenRecorder.Media.Tests;

public sealed class DedicatedProcessIoTests
{
    [Fact]
    public async Task BlockingPipeReadDoesNotOccupyPoolAndCompletionJoinsItsReader()
    {
        using var input = new AnonymousPipeServerStream(PipeDirection.In);
        using var writer = new AnonymousPipeClientStream(PipeDirection.Out, input.ClientSafePipeHandle);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        var value = -1;
        var operation = DedicatedProcessIo.Run(() => {
            entered.SetResult(Thread.CurrentThread.IsThreadPoolThread);
            value = input.ReadByte();
            Volatile.Write(ref completed, true);
        });
        try
        {
            var pool = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(operation.IsCompleted);
            Assert.False(pool); // a pending anonymous-pipe read must not reserve a pool worker
        }
        finally
        {
            writer.WriteByte(42); writer.Flush();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.True(Volatile.Read(ref completed));
        Assert.Equal(42, value);
        Assert.True(input.CanRead); // caller, not the reader, owns this handle
    }

    [Fact]
    public async Task ReaderFailureIsReturnedToOwner()
    {
        var expected = new IOException("owned pipe failed");
        var error = await Assert.ThrowsAsync<IOException>(() => DedicatedProcessIo.Run(() => throw expected));
        Assert.Same(expected, error);
    }

    [Fact]
    public async Task PipeCopyPreservesAsyncOnlyDestinationAndJoinsAtEof()
    {
        using var input = new AnonymousPipeServerStream(PipeDirection.In);
        using var writer = new AnonymousPipeClientStream(PipeDirection.Out, input.ClientSafePipeHandle);
        using var storage = new MemoryStream();
        using var destination = new AsyncDestination(storage);
        var copy = DedicatedProcessIo.CopyAsync(input, destination, flush: true);
        writer.Write(new byte[] { 1, 2, 3, 42 });
        writer.Dispose();
        await copy.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new byte[] { 1, 2, 3, 42 }, storage.ToArray());
        Assert.True(input.CanRead);
        storage.WriteByte(7); // copy did not take ownership of caller's output
    }

    private sealed class AsyncDestination(MemoryStream storage) : Stream
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            storage.Write(bytes.Span);
        }
        public override Task FlushAsync(CancellationToken ct) => storage.FlushAsync(ct);
        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int n) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int n) => throw new NotSupportedException("Destination requires async writes.");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException();
    }
}
