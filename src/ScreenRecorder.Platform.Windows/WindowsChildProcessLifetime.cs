// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.ExceptionServices;

namespace ScreenRecorder.Platform.Windows;

// Native calls are supplied at the OS boundary so lifecycle races can be tested
// deterministically without killing or modifying any unrelated process.
internal static class WindowsChildProcessLifetime
{
    internal static void StopAndJoin(Func<bool> exited, Func<bool> terminate,
        Func<uint, uint> wait, Func<int> lastError)
    {
        if (exited()) return;
        var requested = terminate();
        // Preserve the termination error before another native wait overwrites it.
        var terminationError = requested ? 0 : lastError();
        // Termination is asynchronous, including a concurrent natural exit.
        // Only a signaled process handle confirms that its I/O has finished.
        var status = wait(5000);
        if (status == 0) return;
        if (status == uint.MaxValue) throw new Win32Exception(lastError());
        if (!requested) throw new Win32Exception(terminationError);
        throw new IOException("Media process exit unconfirmed.");
    }

    internal static void Dispose(Action stopAndJoin, params IDisposable[] owned)
    {
        List<Exception>? errors = null;
        try { stopAndJoin(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        foreach (var resource in owned)
        {
            try { resource.Dispose(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors is { Count: 1 }) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors is { Count: > 1 }) throw new AggregateException("Child exit or resource release failed.", errors);
    }
}
