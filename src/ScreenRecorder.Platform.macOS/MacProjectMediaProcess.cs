// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Platform.macOS;

/// <summary>Seekable bound-FD frame extraction. No source or destination path is reopened.</summary>
public sealed class MacProjectMediaProcess : IProjectMediaProcess
{
    private readonly string _executable;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MacProjectMediaProcess(string executable)
    {
        if (!Path.IsPathFullyQualified(executable))
            throw new ArgumentException("Require an absolute executable path.", nameof(executable));
        _executable = executable;
    }

    public async Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> boundInputs,
        FileStream? boundOutput, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        ArgumentNullException.ThrowIfNull(job);
        if (boundInputs.Count != 1 || !boundInputs[0].CanRead || boundInputs[0].CanWrite || !boundInputs[0].CanSeek)
            throw new ArgumentException("Frame extraction requires exactly one read-only seekable source.");
        if (boundOutput is not null && (!boundOutput.CanWrite || !boundOutput.CanSeek ||
            boundOutput.Length != 0 || boundOutput.Position != 0))
            throw new ArgumentException("Output must be a new empty writable stream.");
        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            boundInputs[0].Position = 0;
            return await RunFrameAsync(job, boundInputs[0], boundOutput, ct);
        }
        finally { _gate.Release(); }
    }

    private async Task<ProjectMediaResult> RunFrameAsync(ProjectMediaJob job, FileStream source,
        FileStream? destination, CancellationToken ct)
    {
        using var output = NativePipe.Create();
        using var error = NativePipe.Create();
        var actions = IntPtr.Zero;
        var attributes = IntPtr.Zero;
        var sourceHeld = false;
        var destinationHeld = false;
        var strings = new[] { _executable }.Concat(job.FileDescriptorArguments())
            .Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        var argv = Marshal.AllocHGlobal((strings.Length + 1) * IntPtr.Size);
        int pid;
        try
        {
            for (var i = 0; i < strings.Length; i++) Marshal.WriteIntPtr(argv, i * IntPtr.Size, strings[i]);
            Marshal.WriteIntPtr(argv, strings.Length * IntPtr.Size, IntPtr.Zero);
            Check(posix_spawn_file_actions_init(out actions));
            Check(posix_spawnattr_init(out attributes));
            Check(posix_spawnattr_setflags(ref attributes, 0x4000)); // POSIX_SPAWN_CLOEXEC_DEFAULT
            source.SafeFileHandle.DangerousAddRef(ref sourceHeld);
            destination?.SafeFileHandle.DangerousAddRef(ref destinationHeld);
            Check(posix_spawn_file_actions_adddup2(ref actions, source.SafeFileHandle.DangerousGetHandle().ToInt32(), 0));
            Check(posix_spawn_file_actions_adddup2(ref actions,
                destination?.SafeFileHandle.DangerousGetHandle().ToInt32() ?? output.WriteFd, 1));
            Check(posix_spawn_file_actions_adddup2(ref actions, error.WriteFd, 2));
            Check(posix_spawn(out pid, _executable, ref actions, ref attributes, argv, Marshal.ReadIntPtr(_NSGetEnviron())));
        }
        finally
        {
            if (sourceHeld) source.SafeFileHandle.DangerousRelease();
            if (destinationHeld) destination!.SafeFileHandle.DangerousRelease();
            if (actions != IntPtr.Zero) posix_spawn_file_actions_destroy(ref actions);
            if (attributes != IntPtr.Zero) posix_spawnattr_destroy(ref attributes);
            Marshal.FreeHGlobal(argv);
            foreach (var value in strings) Marshal.FreeCoTaskMem(value);
        }
        output.CloseWriter();
        error.CloseWriter();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15)); // Only single-frame jobs; never a long export deadline.
        var stdout = ReadBoundedAsync(output.Reader, job.ExpectedOutputBytes);
        var stderr = ReadBoundedAsync(error.Reader, ProjectMediaJob.MaximumDiagnosticBytes);
        var killed = false;
        var reaped = false;
        var overQuota = false;
        var status = 0;
        try
        {
            while (true)
            {
                var result = waitpid(pid, out status, 1);
                if (result == pid) { reaped = true; break; }
                if (result < 0)
                {
                    var errno = Marshal.GetLastPInvokeError();
                    if (errno == 4) continue;
                    reaped = errno == 10; // ECHILD: never signal a potentially reused PID.
                    throw new Win32Exception(errno);
                }
                overQuota |= destination is not null && destination.Length > job.ExpectedOutputBytes;
                if (!killed && (timeout.IsCancellationRequested || stdout.IsFaulted || stderr.IsFaulted || overQuota))
                {
                    if (kill(pid, 9) != 0 && Marshal.GetLastPInvokeError() != 3)
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                    killed = true;
                }
                await Task.Delay(10);
            }
            await Task.WhenAll(stdout, stderr);
            timeout.Token.ThrowIfCancellationRequested();
            overQuota |= destination is not null && destination.Length > job.ExpectedOutputBytes;
            if (overQuota) throw new InvalidDataException("Media output limit exceeded.");
            var diagnostic = Encoding.UTF8.GetString(await stderr);
            if (status != 0) throw new InvalidDataException($"Frame decoder status {status}: {diagnostic}");
            var bytes = await stdout;
            if ((destination?.Length ?? bytes.Length) != job.ExpectedOutputBytes)
                throw new InvalidDataException("Missing or incomplete frame at requested source timestamp.");
            return new(bytes, diagnostic);
        }
        finally
        {
            if (!reaped)
            {
                kill(pid, 9);
                while (waitpid(pid, out _, 0) < 0 && Marshal.GetLastPInvokeError() == 4) { }
            }
            // No callback can signal the child after reaping. Caller streams remain caller-owned.
            try { await Task.WhenAll(stdout, stderr); } catch { }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream reader, int limit)
    {
        using var result = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
        {
            if (result.Length + count > limit) throw new InvalidDataException("Media output limit exceeded.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }

    private static void Check(int error) { if (error != 0) throw new Win32Exception(error); }

    private sealed class NativePipe : IDisposable
    {
        private readonly SafeFileHandle _writer;
        public FileStream Reader { get; }
        public int WriteFd => _writer.DangerousGetHandle().ToInt32();
        private NativePipe(SafeFileHandle reader, SafeFileHandle writer)
        {
            _writer = writer;
            Reader = new(reader, FileAccess.Read, 4096, isAsync: false);
        }
        public static NativePipe Create()
        {
            var descriptors = new int[2];
            if (pipe(descriptors) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var reader = new SafeFileHandle((IntPtr)descriptors[0], true);
            var writer = new SafeFileHandle((IntPtr)descriptors[1], true);
            try
            {
                if (descriptors.Any(fd => fd < 3)) throw new InvalidOperationException("Standard descriptors must be open.");
                foreach (var fd in descriptors)
                    if (fcntl(fd, 2, 1) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
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
