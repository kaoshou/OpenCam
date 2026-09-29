// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using Serilog;

namespace ScreenRecorder.Media.Encoders;

public sealed class EncoderProbeRunner : IEncoderProbeRunner
{
    public async Task<EncoderProbeResult> RunAsync(ProcessStartInfo startInfo, TimeSpan executionTimeout,
        CancellationToken cancellationToken = default)
    {
        if (startInfo.UseShellExecute || !startInfo.RedirectStandardOutput || !startInfo.RedirectStandardError)
            throw new ArgumentException("Encoder probes require redirected stdout/stderr without a shell.", nameof(startInfo));

        var watch = Stopwatch.StartNew();
        if (cancellationToken.IsCancellationRequested || executionTimeout <= TimeSpan.Zero)
            return new(cancellationToken.IsCancellationRequested ? EncoderProbeStatus.Cancelled : EncoderProbeStatus.TimedOut,
                null, watch.Elapsed, "", "", true);

        using var process = new Process { StartInfo = startInfo };
        using var lifetime = new CancellationTokenSource();
        var stdout = new BoundedTail();
        var stderr = new BoundedTail();
        var started = false;
        var status = EncoderProbeStatus.StartFailed;
        var cleanupCompleted = true;
        var completion = Task.CompletedTask;
        try
        {
            started = process.Start();
            if (!started) return new(status, null, watch.Elapsed, "", "Process.Start returned false", true);

            // Drain both pipes concurrently, including unterminated very long lines.
            completion = Task.WhenAll(
                DrainAsync(process.StandardOutput, stdout, lifetime.Token),
                DrainAsync(process.StandardError, stderr, lifetime.Token),
                process.WaitForExitAsync(lifetime.Token));
            await completion.WaitAsync(executionTimeout, cancellationToken).ConfigureAwait(false);
            status = process.ExitCode == 0 ? EncoderProbeStatus.Available : EncoderProbeStatus.ProbeFailed;
        }
        catch (TimeoutException) { status = EncoderProbeStatus.TimedOut; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = EncoderProbeStatus.Cancelled;
        }
        catch (Exception ex)
        {
            status = started ? EncoderProbeStatus.ProbeFailed : EncoderProbeStatus.StartFailed;
            stderr.Append(ex.Message.AsSpan());
        }
        finally
        {
            if (started)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    // The process may have exited between HasExited and Kill.
                    stderr.Append(ex.Message.AsSpan());
                    Log.Warning("Encoder probe termination: {Error}", ex.GetType().Name);
                }

                try
                {
                    // Never use the caller's cancelled token for cleanup.
                    await completion.WaitAsync(EncoderProbePolicy.CleanupTimeout).ConfigureAwait(false);
                    cleanupCompleted = process.HasExited;
                }
                catch (Exception ex)
                {
                    cleanupCompleted = completion.IsCompleted && process.HasExited;
                    Log.Warning("Encoder probe cleanup: {Error}; complete={CleanupCompleted}", ex.GetType().Name, cleanupCompleted);
                }
            }
            lifetime.Cancel();
            // Observe a late pipe failure even if the OS could not finish cleanup.
            _ = completion.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        if (!cleanupCompleted && status == EncoderProbeStatus.Available) status = EncoderProbeStatus.ProbeFailed;
        return new(status, started && process.HasExited ? process.ExitCode : null, watch.Elapsed,
            stdout.Snapshot(), stderr.Snapshot(), cleanupCompleted);
    }

    private static async Task DrainAsync(StreamReader reader, BoundedTail tail, CancellationToken token)
    {
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
            tail.Append(buffer.AsSpan(0, count));
    }

    private sealed class BoundedTail
    {
        private readonly StringBuilder _text = new();
        public void Append(ReadOnlySpan<char> value)
        {
            lock (_text)
            {
                if (value.Length > EncoderProbePolicy.TailCharacterLimit)
                    value = value[^EncoderProbePolicy.TailCharacterLimit..];
                var excess = _text.Length + value.Length - EncoderProbePolicy.TailCharacterLimit;
                if (excess > 0) _text.Remove(0, excess);
                _text.Append(value);
            }
        }
        public string Snapshot() { lock (_text) return _text.ToString(); }
    }
}
