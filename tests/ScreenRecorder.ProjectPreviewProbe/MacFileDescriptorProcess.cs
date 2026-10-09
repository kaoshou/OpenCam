// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ScreenRecorder.ProjectPreviewProbe;

internal static class MacFileDescriptorProcess
{
    // Diagnostic only: one FFmpeg child, no shell, no reopening source paths, no product integration.
    internal static async Task<string> RunAsync(string executable, string[] arguments, FileStream source, CancellationToken ct)
        => await RunStreamingAsync(executable, arguments, source, async (output, error, token, abort) =>
        {
            using var stdout = new StreamReader(output, leaveOpen: true);
            using var stderr = new StreamReader(error, leaveOpen: true);
            var text = ReadBoundedAsync(stdout);
            var diagnostics = ReadBoundedAsync(stderr);
            await JoinReadersAsync(text, diagnostics, abort);
            return await text;
        }, ct);

    // Callback must honor cancellation. Diagnostic has a fixed 15-second lifetime, not a product playback limit.
    internal static async Task<T> RunStreamingAsync<T>(string executable, string[] arguments, FileStream source,
        Func<Stream, Stream, CancellationToken, Action, Task<T>> consume, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("Require an absolute executable path.");
        if (!source.CanRead || source.CanWrite || !source.CanSeek) throw new ArgumentException("Require a read-only regular source.");
        ct.ThrowIfCancellationRequested();
        using var output = NativePipe.Create();
        using var error = NativePipe.Create();
        var actions = IntPtr.Zero;
        var attributes = IntPtr.Zero;
        var added = false;
        var sourceHandle = source.SafeFileHandle;
        var strings = new[] { executable }.Concat(arguments).Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        var argv = Marshal.AllocHGlobal((strings.Length + 1) * IntPtr.Size);
        int pid;
        try
        {
            for (var i = 0; i < strings.Length; i++) Marshal.WriteIntPtr(argv, i * IntPtr.Size, strings[i]);
            Marshal.WriteIntPtr(argv, strings.Length * IntPtr.Size, IntPtr.Zero);
            Check(posix_spawn_file_actions_init(out actions));
            Check(posix_spawnattr_init(out attributes));
            Check(posix_spawnattr_setflags(ref attributes, 0x4000)); // CLOEXEC_DEFAULT: only explicit dup2 actions survive.
            sourceHandle.DangerousAddRef(ref added);
            Check(posix_spawn_file_actions_adddup2(ref actions, sourceHandle.DangerousGetHandle().ToInt32(), 0));
            Check(posix_spawn_file_actions_adddup2(ref actions, output.WriteFd, 1));
            Check(posix_spawn_file_actions_adddup2(ref actions, error.WriteFd, 2));
            Check(posix_spawn(out pid, executable, ref actions, ref attributes, argv, Marshal.ReadIntPtr(_NSGetEnviron())));
        }
        finally
        {
            if (added) sourceHandle.DangerousRelease();
            if (actions != IntPtr.Zero) posix_spawn_file_actions_destroy(ref actions);
            if (attributes != IntPtr.Zero) posix_spawnattr_destroy(ref attributes);
            Marshal.FreeHGlobal(argv);
            foreach (var value in strings) Marshal.FreeCoTaskMem(value);
        }
        output.CloseWriter();
        error.CloseWriter();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        // Only this loop signals/reaps the child. A cancellation callback must never kill a reused PID after waitpid.
        var consumed = consume(output.Reader.BaseStream, error.Reader.BaseStream, timeout.Token, () => timeout.Cancel());
        var killed = false;
        var reaped = false;
        var status = 0;
        try
        {
            while (true)
            {
                var result = waitpid(pid, out status, 1); // WNOHANG
                if (result == pid) { reaped = true; break; }
                if (result < 0)
                {
                    var errno = Marshal.GetLastPInvokeError();
                    if (errno == 4) continue; // EINTR
                    // No ownership after ECHILD; never signal a potentially reused PID.
                    reaped = errno == 10;
                    throw new Win32Exception(errno);
                }
                if (!killed && (timeout.IsCancellationRequested || consumed.IsFaulted || consumed.IsCanceled))
                {
                    CheckSignal(kill(pid, 9));
                    killed = true;
                }
                await Task.Delay(10);
            }
            var resultValue = await consumed;
            timeout.Token.ThrowIfCancellationRequested();
            if (status != 0) throw new InvalidDataException($"Media child status {status}");
            return resultValue;
        }
        finally
        {
            timeout.Cancel();
            if (!reaped)
            {
                kill(pid, 9);
                while (waitpid(pid, out _, 0) < 0 && Marshal.GetLastPInvokeError() == 4) { }
            }
            // Drains terminate at EOF after child exit, including output-limit and cancellation failures.
            try { await consumed; } catch { }
        }
    }

    // A failed reader requests child termination before joining the other pipe reader.
    // Returning early would leave an outstanding read racing disposal; WhenAll alone can wait on a blocked child.
    internal static async Task JoinReadersAsync(Task first, Task second, Action abort)
    {
        try
        {
            await await Task.WhenAny(first, second);
            await Task.WhenAll(first, second);
        }
        catch
        {
            abort();
            try { await Task.WhenAll(first, second); } catch { }
            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
        {
            if (text.Length + count > 1024 * 1024) throw new InvalidDataException("Media output limit exceeded.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    private static void Check(int error) { if (error != 0) throw new Win32Exception(error); }
    private static void CheckSignal(int result)
    {
        if (result != 0 && Marshal.GetLastPInvokeError() != 3) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private sealed class NativePipe : IDisposable
    {
        private readonly SafeFileHandle _writer;
        public StreamReader Reader { get; }
        public int WriteFd => _writer.DangerousGetHandle().ToInt32();
        private NativePipe(SafeFileHandle reader, SafeFileHandle writer)
        {
            _writer = writer;
            Reader = new(new FileStream(reader, FileAccess.Read, 4096, isAsync: false));
        }
        public static NativePipe Create()
        {
            var descriptors = new int[2];
            if (pipe(descriptors) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var reader = new SafeFileHandle((IntPtr)descriptors[0], true);
            var writer = new SafeFileHandle((IntPtr)descriptors[1], true);
            try
            {
                if (descriptors.Any(fd => fd < 3)) throw new InvalidOperationException("Probe requires normal standard descriptors.");
                foreach (var fd in descriptors)
                    if (fcntl(fd, 2, 1) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError()); // F_SETFD, FD_CLOEXEC
                return new(reader, writer);
            }
            catch { reader.Dispose(); writer.Dispose(); throw; }
        }
        public void CloseWriter() => _writer.Dispose();
        public void Dispose() { Reader.Dispose(); _writer.Dispose(); }
    }

    private const string Lib = "/usr/lib/libSystem.B.dylib";
    [DllImport(Lib)] private static extern int posix_spawn_file_actions_init(out IntPtr actions);
    [DllImport(Lib)] private static extern int posix_spawn_file_actions_destroy(ref IntPtr actions);
    [DllImport(Lib)] private static extern int posix_spawn_file_actions_adddup2(ref IntPtr actions, int source, int target);
    [DllImport(Lib)] private static extern int posix_spawnattr_init(out IntPtr attributes);
    [DllImport(Lib)] private static extern int posix_spawnattr_destroy(ref IntPtr attributes);
    [DllImport(Lib)] private static extern int posix_spawnattr_setflags(ref IntPtr attributes, short flags);
    [DllImport(Lib)] private static extern int posix_spawn(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        ref IntPtr actions, ref IntPtr attributes, IntPtr argv, IntPtr environment);
    [DllImport(Lib)] private static extern IntPtr _NSGetEnviron();
    [DllImport(Lib, SetLastError = true)] private static extern int pipe([Out] int[] descriptors);
    [DllImport(Lib, SetLastError = true)] private static extern int fcntl(int fd, int command, int argument);
    [DllImport(Lib, SetLastError = true)] private static extern int waitpid(int pid, out int status, int options);
    [DllImport(Lib, SetLastError = true)] private static extern int kill(int pid, int signal);
}
