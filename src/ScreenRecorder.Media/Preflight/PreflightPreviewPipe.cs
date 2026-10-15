// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;

namespace ScreenRecorder.Media.Preflight;

public sealed class PreflightPreviewPipe : IAsyncDisposable
{
    public const int Width = 960, Height = 540;
    private readonly Process _process;
    private readonly Task _frames, _errors;
    private byte[]? _latest;
    private long _count;
    private int _closed;
    public PreflightPreviewPipe(string executable)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "pipe",
            "-f", "matroska", "-i", "pipe:0", "-an", "-vf",
            "fps=4,scale=960:540:force_original_aspect_ratio=decrease,pad=960:540:(ow-iw)/2:(oh-ih)/2",
            "-pix_fmt", "bgra", "-c:v", "rawvideo", "-threads", "1", "-f", "rawvideo", "pipe:1" }) start.ArgumentList.Add(argument);
        _process = Process.Start(start) ?? throw new IOException("Cannot start preflight preview decoder.");
        _frames = ReadFramesAsync();
        _errors = DrainErrorsAsync();
    }
    public Stream Input => _process.StandardInput.BaseStream;
    public byte[]? LatestFrame => Volatile.Read(ref _latest);
    public long FrameCount => Interlocked.Read(ref _count);
    public bool HasExited => _process.HasExited;
    private async Task ReadFramesAsync()
    {
        var stream = _process.StandardOutput.BaseStream;
        while (true)
        {
            var frame = new byte[Width * Height * 4];
            var length = 0;
            while (length < frame.Length)
            {
                var read = await stream.ReadAsync(frame.AsMemory(length));
                if (read == 0)
                {
                    if (length != 0) throw new InvalidDataException("Incomplete preflight frame.");
                    return;
                }
                length += read;
            }
            Volatile.Write(ref _latest, frame);
            Interlocked.Increment(ref _count);
        }
    }
    private async Task DrainErrorsAsync()
    {
        var buffer = new char[2048];
        while (await _process.StandardError.ReadAsync(buffer) != 0) { }
    }
    public async Task CompleteAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0) Input.Close();
        await _process.WaitForExitAsync(ct);
        await Task.WhenAll(_frames, _errors).WaitAsync(ct);
        if (_process.ExitCode != 0) throw new InvalidDataException("Preflight preview decoding failed.");
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(true);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            // Join pipe readers even on interrupted decoding; kill/close may leave
            // an incomplete last frame, which is never published.
            try { await Task.WhenAll(_frames, _errors); }
            catch (InvalidDataException) when (_process.HasExited) { }
        }
        finally { _process.Dispose(); }
    }
}
