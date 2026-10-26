// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Media.FFmpeg;

// The process owner must terminate/join its child before joining blocking pipe
// reads. This operation owns neither the stream nor the child.
internal static class DedicatedProcessIo
{
    internal static bool RequiresDedicatedReader(Stream source) =>
        OperatingSystem.IsWindows() && source is FileStream { IsAsync: false };

    internal static Task Run(Action operation)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() => {
            try { operation(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "OpenCam process pipe" }.Start();
        return completion.Task;
    }

    internal static Task CopyAsync(Stream source, Stream destination,
        Action<int, double>? written = null, bool flush = false) => Run(() => {
        var buffer = new byte[81920];
        int count;
        while ((count = source.Read(buffer)) > 0)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            // Keep the caller's asynchronous destination contract, including
            // async-only streams, without reserving a pool thread for pipe reads.
            destination.WriteAsync(buffer.AsMemory(0, count)).AsTask().GetAwaiter().GetResult();
            written?.Invoke(count, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        if (flush) destination.FlushAsync().GetAwaiter().GetResult();
    });
}
