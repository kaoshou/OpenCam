// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Media.Tests;

public partial class FFmpegStartupHandshakeTests
{
    [Fact]
    public async Task ProgressPipeIsReadAsynchronouslyWithoutBlockingForEndOfStream()
    {
        // This test feeds stderr directly; no external encoder is launched.
        await using var engine = new FFmpegScreenRecorderEngine(new StubProvider(), ffmpegPath: Environment.ProcessPath!);
        using var process = Process.GetCurrentProcess();
        using var stream = new AsyncOnlyProgressStream(
            "frame=1\nout_time=00:00:00.033333\nprogress=continue\n");
        using var reader = new StreamReader(stream);
        var errors = new List<string>();
        engine.EngineErrorOccurred += (_, error) => errors.Add(error);

        await engine.ReadStderrLoop(process, reader, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Equal(1, engine.CurrentFramesRecorded);
        Assert.InRange(engine.CurrentRecordedTime.TotalMilliseconds, 32, 34);
    }

    // Real StreamReader over a pipe-like stream: synchronous reads must not
    // occupy a worker while waiting for FFmpeg's next progress report.
    private sealed class AsyncOnlyProgressStream(string text) : Stream
    {
        private readonly MemoryStream _bytes = new(Encoding.UTF8.GetBytes(text));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous pipe read blocks startup progress.");
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return _bytes.Read(buffer.Span);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _bytes.Dispose(); base.Dispose(disposing); }
    }
}
