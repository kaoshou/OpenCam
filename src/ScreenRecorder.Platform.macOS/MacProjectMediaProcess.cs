// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Media.FFmpeg;

namespace ScreenRecorder.Platform.macOS;

/// <summary>Seekable bound-FD frame / bounded PCM extraction. No media path is reopened.</summary>
public sealed class MacProjectMediaProcess : IProjectMediaProcess, IProjectStreamingMediaProcess
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
        if (boundInputs.Count != job.InputCount || boundInputs.Count is < 1 or > 129 || boundInputs.Any(s => !s.CanRead || s.CanWrite || !s.CanSeek))
            throw new ArgumentException("Media job requires read-only seekable bound sources.");
        if (job.RequiresOutput && boundOutput is null)
            throw new ArgumentException("This media job requires a bound output stream.");
        if (boundOutput is not null && (!boundOutput.CanWrite || !boundOutput.CanSeek ||
            boundOutput.Length != 0 || boundOutput.Position != 0))
            throw new ArgumentException("Output must be a new empty writable stream.");
        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            foreach (var input in boundInputs) input.Position = 0;
            return await RunBoundAsync(job, boundInputs, boundOutput, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task RunStreamingAsync(ProjectMediaJob job, FileStream boundInput,
        Func<Stream, CancellationToken, Task> consume, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        ArgumentNullException.ThrowIfNull(consume);
        if (job.InputCount != 1 || !boundInput.CanRead || boundInput.CanWrite || !boundInput.CanSeek)
            throw new ArgumentException("Streaming requires one bound read-only source.");
        await _gate.WaitAsync(ct);
        try
        {
            boundInput.Position = 0;
            await RunBoundAsync(job, [boundInput], null, ct, consume);
        }
        finally { _gate.Release(); }
    }

    private async Task<ProjectMediaResult> RunBoundAsync(ProjectMediaJob job, IReadOnlyList<FileStream> sources,
        FileStream? destination, CancellationToken ct, Func<Stream, CancellationToken, Task>? consume = null)
    {
        using var output = NativePipe.Create();
        using var error = NativePipe.Create();
        var actions = IntPtr.Zero;
        var attributes = IntPtr.Zero;
        var sourceHeld = new bool[sources.Count];
        // Exposing FileStream.SafeFileHandle synchronizes its managed position back to
        // the native descriptor on Unix. Never expose it again while a child is reading:
        // doing so rewinds the shared descriptor and repeats input indefinitely.
        var sourceHandles = sources.Select(source => source.SafeFileHandle).ToArray();
        var sourceFds = sourceHandles.Select(handle => handle.DangerousGetHandle().ToInt32()).ToArray();
        var destinationHandle = destination?.SafeFileHandle;
        var destinationHeld = false;
        var relocated = new List<SafeFileHandle>();
        var executable = job.IsProbe
            ? FFmpegDiscovery.FindFFprobeExecutable() ?? throw new FileNotFoundException("ffprobe is required for export verification.")
            : _executable;
        var strings = new[] { executable }.Concat(job.FileDescriptorArguments())
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
            for (var i = 0; i < sources.Count; i++) sourceHandles[i].DangerousAddRef(ref sourceHeld[i]);
            destinationHandle?.DangerousAddRef(ref destinationHeld);
            int Relocate(int fd)
            {
                // Darwin arm64 passes the variadic argument on the stack, not x2.
                var copy = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? fcntlArm64(fd, 67, 0, 0, 0, 0, 0, 0, sources.Count + 3)
                    : fcntl(fd, 67, sources.Count + 3); // F_DUPFD_CLOEXEC, outside child mapping range.
                if (copy < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                relocated.Add(new SafeFileHandle((IntPtr)copy, true));
                return copy;
            }
            var safeSources = sourceFds.Select(Relocate).ToArray();
            var safeOutput = Relocate(destinationHandle?.DangerousGetHandle().ToInt32() ?? output.WriteFd);
            var safeError = Relocate(error.WriteFd);
            Check(posix_spawn_file_actions_adddup2(ref actions, safeSources[0], 0));
            Check(posix_spawn_file_actions_adddup2(ref actions,
                safeOutput, 1));
            Check(posix_spawn_file_actions_adddup2(ref actions, safeError, 2));
            for (var i = 1; i < safeSources.Length; i++)
                Check(posix_spawn_file_actions_adddup2(ref actions, safeSources[i], i + 2));
            Check(posix_spawn(out pid, executable, ref actions, ref attributes, argv, Marshal.ReadIntPtr(_NSGetEnviron())));
        }
        finally
        {
            foreach (var copy in relocated) copy.Dispose();
            for (var i = 0; i < sources.Count; i++) if (sourceHeld[i]) sourceHandles[i].DangerousRelease();
            if (destinationHeld) destinationHandle!.DangerousRelease();
            if (actions != IntPtr.Zero) posix_spawn_file_actions_destroy(ref actions);
            if (attributes != IntPtr.Zero) posix_spawnattr_destroy(ref attributes);
            Marshal.FreeHGlobal(argv);
            foreach (var value in strings) Marshal.FreeCoTaskMem(value);
        }
        output.CloseWriter();
        error.CloseWriter();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!job.IsLongRunning) timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var limit = job.OutputLimit > 0 ? job.OutputLimit : job.ExpectedOutputBytes;
        var counted = new CountingReadStream(output.Reader, limit);
        async Task<byte[]> ConsumeAsync()
        {
            await consume!(counted, timeout.Token);
            return [];
        }
        var stdout = consume is null
            ? ReadBoundedAsync(output.Reader, destination is null ? checked((int)limit) : 0)
            : ConsumeAsync();
        var stderr = ReadBoundedAsync(error.Reader, ProjectMediaJob.MaximumDiagnosticBytes);
        var killed = false;
        var reaped = false;
        var overQuota = false;
        var status = 0;
        var activity = System.Diagnostics.Stopwatch.StartNew();
        long lastProgress = -1;
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
                var progress = (destination?.Length ?? 0) + counted.Count + sourceFds.Sum(fd => lseek(fd, 0, 1));
                if (progress != lastProgress) { lastProgress = progress; activity.Restart(); }
                if (job.IsLongRunning && activity.Elapsed > TimeSpan.FromSeconds(30)) timeout.Cancel();
                overQuota |= destination is not null && destination.Length > limit;
                if (!killed && (timeout.IsCancellationRequested || stdout.IsFaulted || stdout.IsCanceled || stderr.IsFaulted || overQuota))
                {
                    timeout.Cancel();
                    if (kill(pid, 9) != 0 && Marshal.GetLastPInvokeError() != 3)
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                    killed = true;
                }
                await Task.Delay(10);
            }
            await Task.WhenAll(stdout, stderr);
            overQuota |= destination is not null && destination.Length > limit;
            if (overQuota) throw new InvalidDataException("Media output limit exceeded.");
            timeout.Token.ThrowIfCancellationRequested();
            var diagnostic = Encoding.UTF8.GetString(await stderr);
            if (status != 0) throw new InvalidDataException($"Media decoder status {status}: {diagnostic}");
            var bytes = await stdout;
            var length = destination?.Length ?? (consume is null ? bytes.LongLength : counted.Count);
            var exact = job.OutputLimit > 0 ? job.ExactLength : job.ExpectedOutputBytes;
            if (length <= 0 || (exact is { } expected && length != expected))
                throw new InvalidDataException("Missing or incomplete media at requested source interval.");
            return new(bytes, diagnostic);
        }
        finally
        {
            timeout.Cancel();
            if (!reaped)
            {
                kill(pid, 9);
                while (waitpid(pid, out _, 0) < 0 && Marshal.GetLastPInvokeError() == 4) { }
            }
            // No callback can signal the child after reaping. Caller streams remain caller-owned.
            try { await Task.WhenAll(stdout, stderr); } catch { }
        }
    }

    private sealed class CountingReadStream(Stream inner, long limit) : Stream
    {
        private long _count;
        public long Count => Interlocked.Read(ref _count);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var read = await inner.ReadAsync(buffer, ct);
            if (Interlocked.Add(ref _count, read) > limit) throw new InvalidDataException("Streaming media output limit exceeded.");
            return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
    [DllImport(Lib, EntryPoint = "fcntl", SetLastError = true)]
    private static extern int fcntlArm64(int fd, int command, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, nint argument);
    [DllImport(Lib, SetLastError = true)] private static extern int waitpid(int pid, out int status, int options);
    [DllImport(Lib, SetLastError = true)] private static extern int kill(int pid, int signal);
    [DllImport(Lib, SetLastError = true)] private static extern long lseek(int fd, long offset, int whence);
}
