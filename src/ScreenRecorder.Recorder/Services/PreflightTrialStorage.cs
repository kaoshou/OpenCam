// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Recorder.Services;

public sealed class PreflightTrialStorage : IAsyncDisposable
{
    private readonly BoundDirectory _directory;
    private readonly string _name = Guid.NewGuid().ToString("N") + ".mkv";
    private FileStream? _file;
    private readonly Stream _writer;
    private bool _disposed;
    public PreflightTrialStorage(string? root = null)
    {
        using var parent = BoundDirectory.Open(root ?? Path.Combine(Path.GetTempPath(), "OpenCam", "preflight"), create: true);
        _directory = parent.OpenChild(Guid.NewGuid().ToString("N"), create: true, exclusive: true);
        DirectoryPath = _directory.CurrentPath;
        try { _file = _directory.CreateNew(_name); _writer = new LimitedWriter(_file); }
        catch { _directory.Dispose(); throw; }
    }
    public string DirectoryPath { get; }
    public Stream Writer => _writer;
    public string Sha256 { get; private set; } = "";
    public long Length { get; private set; }
    public async Task SealAsync(CancellationToken ct)
    {
        if (_file is not { } file) return;
        file.Flush(true);
        file.Position = 0;
        Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
        Length = file.Length;
        await file.DisposeAsync();
        _file = null;
    }
    public FileStream OpenRead()
    {
        if (Sha256.Length == 0) throw new InvalidOperationException("Trial is not closed.");
        var file = _directory.Read(_name);
        try
        {
            if (file.Length != Length || Convert.ToHexString(SHA256.HashData(file)) != Sha256)
                throw new InvalidDataException("Trial file changed; refusing access or cleanup.");
            file.Position = 0;
            return file;
        }
        catch { file.Dispose(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try
        {
            await SealAsync(default);
            using (OpenRead()) { }
            _directory.DeleteOwnedFile(_name);
        }
        finally { _directory.Dispose(); _disposed = true; }
        // Never recurse; unexpected files are preserved and cleanup failure reported.
        Directory.Delete(DirectoryPath, recursive: false);
    }

    private sealed class LimitedWriter(FileStream inner) : Stream
    {
        private const long Limit = 256L * 1024 * 1024;
        private void Check(int count) { if (inner.Position + count > Limit) throw new IOException("Preflight trial exceeds256MiB limit."); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); inner.Write(buffer, offset, count); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { Check(buffer.Length); await inner.WriteAsync(buffer, ct); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
