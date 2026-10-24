// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Pipes;

namespace ScreenRecorder.Media.Capture;

/// <summary>One owned Windows video pipe. Its fixed copy buffer never retains recording history.</summary>
internal sealed class NativeVideoPipe : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _copy, _start;
    private int _disposed;
    public string Name { get; } = "OCv" + Guid.NewGuid().ToString("N");
    public string VideoInput => $"-thread_queue_size 8 -analyzeduration 0 -probesize 32768 -f matroska -i \"\\\\.\\pipe\\{Name}\" ";
    public event EventHandler? CopyFailed;

    public NativeVideoPipe()
    {
        _pipe = new(Name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 0, 81920);
    }

    public Task StartAsync(INativeVideoCapture capture, CancellationToken token) =>
        _start = StartCoreAsync(capture, token);

    private async Task StartCoreAsync(INativeVideoCapture capture, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await _pipe.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);
        await capture.BeginAsync(deadline.Token).ConfigureAwait(false);
        _copy = CopyAsync(capture.Output);
    }

    private async Task CopyAsync(Stream source)
    {
        try
        {
            await source.CopyToAsync(_pipe, 81920, _cancellation.Token).ConfigureAwait(false);
            await _pipe.FlushAsync(_cancellation.Token).ConfigureAwait(false);
        }
        catch when (_cancellation.IsCancellationRequested) { }
        catch
        {
            CopyFailed?.Invoke(this, EventArgs.Empty);
            throw new IOException("Native video pipe transport failed.");
        }
        finally { _pipe.Dispose(); }
    }

    public async Task CompleteAsync()
    {
        if (_copy is not null) await _copy.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancellation.Cancel();
        _pipe.Dispose();
        try
        {
            if (_start is not null) await _start.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            if (_copy is not null) await _copy.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or IOException) { }
        finally { _cancellation.Dispose(); }
    }
}
