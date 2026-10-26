// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using Serilog;
using ScreenRecorder.Media.FFmpeg;

namespace ScreenRecorder.Media.Preflight;

public sealed class PreflightPreviewPipe : IAsyncDisposable
{
    public const int Width = 960, Height = 540;
    private readonly Process _process;
    private readonly Task _frames, _errors;
    private byte[]? _latest;
    private long _count;
    private int _closed, _disposing;
    public PreflightPreviewPipe(string executable)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "pipe",
            "-f", "matroska", "-i", "pipe:0", "-an", "-vf",
            "fps=4,scale=960:540:force_original_aspect_ratio=decrease,pad=960:540:(ow-iw)/2:(oh-ih)/2",
            "-pix_fmt", "rgba", "-c:v", "rawvideo", "-threads", "1", "-f", "rawvideo", "pipe:1" }) start.ArgumentList.Add(argument);
        _process = Process.Start(start) ?? throw new IOException("Cannot start preflight preview decoder.");
        _frames = OperatingSystem.IsWindows() ? DedicatedProcessIo.Run(ReadFrames) : ReadFramesAsync();
        _errors = DrainErrorsAsync();
    }
    public Stream Input => _process.StandardInput.BaseStream;
    public byte[]? LatestFrame => Volatile.Read(ref _latest);
    public long FrameCount => Interlocked.Read(ref _count);
    public bool HasExited => _process.HasExited;
    private void ReadFrames()
    {
        var stream = _process.StandardOutput.BaseStream;
        while (true)
        {
            var frame = new byte[Width * Height * 4];
            var length = 0;
            while (length < frame.Length)
            {
                var read = stream.Read(frame.AsSpan(length));
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
        var tail = new StringBuilder();
        void Append(int read)
        {
            tail.Append(buffer, 0, read);
            if (tail.Length > 4096) tail.Remove(0, tail.Length - 4096);
        }
        if (OperatingSystem.IsWindows())
            await DedicatedProcessIo.Run(() => {
                int count;
                while ((count = _process.StandardError.Read(buffer)) != 0) Append(count);
            }).ConfigureAwait(false);
        else
        {
            int count;
            while ((count = await _process.StandardError.ReadAsync(buffer)) != 0) Append(count);
        }
        await _process.WaitForExitAsync();
        if (_process.ExitCode != 0 && Volatile.Read(ref _disposing) == 0)
            Log.Warning("Preflight preview decoder exited: ExitCode={ExitCode}, Frames={Frames}, Stderr={Stderr}",
                _process.ExitCode, FrameCount, tail.ToString());
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
        Interlocked.Exchange(ref _disposing, 1);
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
