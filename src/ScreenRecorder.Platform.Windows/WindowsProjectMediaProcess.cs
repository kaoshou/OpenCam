// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Platform.Windows;

/// <summary>FFmpeg receives only the bound file handles and private output pipes. No source path is reopened.</summary>
public sealed class WindowsProjectMediaProcess : IProjectMediaProcess, IProjectStreamingMediaProcess
{
    private readonly string executable;
    private readonly SemaphoreSlim gate = new(1, 1);
    public WindowsProjectMediaProcess(string executable)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("Absolute executable required.");
        this.executable = executable;
    }

    public Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> boundInputs,
        FileStream? boundOutput, CancellationToken ct) => Run(job, boundInputs, boundOutput, null, ct);

    public async Task RunStreamingAsync(ProjectMediaJob job, FileStream boundInput,
        Func<Stream, CancellationToken, Task> consume, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(consume);
        await Run(job, [boundInput], null, consume, ct);
    }

    private async Task<ProjectMediaResult> Run(ProjectMediaJob job, IReadOnlyList<FileStream> inputs,
        FileStream? destination, Func<Stream, CancellationToken, Task>? consume, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (inputs.Count != job.InputCount || inputs.Count is < 1 or > 129 ||
            inputs.Any(s => !s.CanRead || s.CanWrite || !s.CanSeek))
            throw new ArgumentException("Media requires read-only seekable bound sources.");
        if (job.RequiresOutput && destination is null)
            throw new ArgumentException("Media job requires a bound destination.");
        if (destination is not null && (!destination.CanWrite || !destination.CanSeek ||
            destination.Length != 0 || destination.Position != 0))
            throw new ArgumentException("Destination must be new, empty and writable.");
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        await gate.WaitAsync(ct);
        try
        {
            foreach (var source in inputs) source.Position = 0;
            var tool = job.IsProbe ? FFmpegDiscovery.FindFFprobeExecutable()
                ?? throw new FileNotFoundException("ffprobe is required.") : executable;
            using var child = BoundChild.Start(tool, job.FileDescriptorArguments(), inputs, destination);
            using var life = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (!job.IsLongRunning) life.CancelAfter(TimeSpan.FromSeconds(15));
            var limit = job.OutputLimit > 0 ? job.OutputLimit : job.ExpectedOutputBytes;
            using var counted = new LimitedReadStream(child.Output, limit);
            var errorTask = ReadBounded(child.Error, ProjectMediaJob.MaximumDiagnosticBytes, life.Token);
            using var bytes = new MemoryStream();
            var outputTask = consume is null
                ? counted.CopyToAsync(bytes, life.Token) : consume(counted, life.Token);
            var activity = Stopwatch.StartNew();
            long last = -1;
            try
            {
                while (!child.Exited)
                {
                    life.Token.ThrowIfCancellationRequested();
                    if (outputTask.IsCompleted) await outputTask;
                    if (errorTask.IsCompleted) await errorTask;
                    var progress = counted.Count + (destination?.Length ?? 0) + child.InputPosition;
                    if (last != progress) { last = progress; activity.Restart(); }
                    if (destination is not null && destination.Length > limit)
                        throw new InvalidDataException("Media output limit exceeded.");
                    if (job.IsLongRunning && activity.Elapsed > TimeSpan.FromSeconds(30))
                        throw new TimeoutException("Media process stopped making progress.");
                    await Task.Delay(10, life.Token);
                }
                await Task.WhenAll(outputTask, errorTask);
                life.Token.ThrowIfCancellationRequested();
                var diagnostic = Encoding.UTF8.GetString(await errorTask);
                if (child.ExitCode != 0) throw new InvalidDataException($"Media exit {child.ExitCode}: {diagnostic}");
                var length = destination?.Length ?? counted.Count;
                var exact = job.OutputLimit > 0 ? job.ExactLength : job.ExpectedOutputBytes;
                if (length <= 0 || length > limit || (exact is { } n && length != n))
                    throw new InvalidDataException("Missing, oversized or incomplete media output.");
                return new(bytes.ToArray(), diagnostic);
            }
            finally
            {
                life.Cancel();
                child.StopAndJoin();
                try { await Task.WhenAll(outputTask, errorTask); }
                catch (Exception ex) { Debug.WriteLine($"Media pipes closed: {ex.GetType().Name}"); }
            }
        }
        finally { gate.Release(); }
    }

    private static async Task<byte[]> ReadBounded(Stream stream, int limit, CancellationToken ct)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + n > limit) throw new InvalidDataException("Media diagnostic limit exceeded.");
            bytes.Write(buffer, 0, n);
        }
        return bytes.ToArray();
    }

    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private long count;
        public long Count => Interlocked.Read(ref count);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await inner.ReadAsync(buffer, ct);
            if (Interlocked.Add(ref count, n) > limit) throw new InvalidDataException("Media output limit exceeded.");
            return n;
        }
        public override Task<int> ReadAsync(byte[] b, int o, int n, CancellationToken ct) => ReadAsync(b.AsMemory(o, n), ct).AsTask();
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int n) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int n) => throw new NotSupportedException();
    }

    private sealed class BoundChild : IDisposable
    {
        private readonly SafeProcessHandle process;
        private readonly SafeFileHandle[] sources;
        public FileStream Output { get; }
        public FileStream Error { get; }
        private BoundChild(SafeProcessHandle process, SafeFileHandle[] sources, FileStream output, FileStream error)
            => (this.process, this.sources, Output, Error) = (process, sources, output, error);
        public bool Exited => WaitForSingleObject(process, 0) switch {
            0 => true, 258 => false, _ => throw new Win32Exception(Marshal.GetLastPInvokeError()) };
        public uint ExitCode { get { Check(GetExitCodeProcess(process, out var code)); return code; } }
        public long InputPosition => sources.Sum(s => { Check(SetFilePointerEx(s, 0, out var pos, 1)); return pos; });

        public static BoundChild Start(string exe, string[] args, IReadOnlyList<FileStream> sources, FileStream? output)
        {
            // STARTUPINFO's CRT descriptor table maps 0/1/2 and optional 3. Binary mode is
            // essential: text translation would corrupt RGBA, PCM and MP4. The explicit
            // handle list excludes every unrelated recorder handle from the child.
            var owned = new List<SafeFileHandle>();
            var attribute = IntPtr.Zero;
            var handlesMemory = IntPtr.Zero;
            var crt = IntPtr.Zero;
            var initialized = false;
            FileStream? stdout = null, stderr = null;
            SafeProcessHandle? childHandle = null;
            try
            {
                SafeFileHandle Dup(SafeFileHandle handle) {
                    Check(DuplicateHandle(GetCurrentProcess(), handle, GetCurrentProcess(), out var duplicate, 0, true, 2));
                    owned.Add(duplicate); return duplicate;
                }
                (FileStream Read, SafeFileHandle Write) Pipe() {
                    Check(CreatePipe(out var read, out var write, IntPtr.Zero, 0));
                    using (write) {
                        try { return (new FileStream(read, FileAccess.Read, 4096, false), Dup(write)); }
                        catch { read.Dispose(); throw; }
                    }
                }
                var inputHandles = sources.Select(s => Dup(s.SafeFileHandle)).ToArray();
                var outPipe = Pipe(); stdout = outPipe.Read;
                var errPipe = Pipe(); stderr = errPipe.Read;
                var descriptors = new List<SafeFileHandle> { inputHandles[0], output is null ? outPipe.Write : Dup(output.SafeFileHandle), errPipe.Write };
                descriptors.AddRange(inputHandles.Skip(1));
                var count = descriptors.Count;
                var handles = descriptors.Select(h => h.DangerousGetHandle()).ToArray();
                var bytes = 4 + count + count * IntPtr.Size;
                crt = Marshal.AllocHGlobal(bytes);
                Marshal.WriteInt32(crt, count);
                for (var i = 0; i < count; i++) {
                    Marshal.WriteByte(crt, 4 + i, (byte)(i == 2 || (i == 1 && output is null) ? 9 : 1));
                    Marshal.WriteIntPtr(crt, 4 + count + i * IntPtr.Size, handles[i]);
                }
                nuint size = 0;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                attribute = Marshal.AllocHGlobal(checked((int)size));
                Check(InitializeProcThreadAttributeList(attribute, 1, 0, ref size)); initialized = true;
                handlesMemory = Marshal.AllocHGlobal(count * IntPtr.Size);
                Marshal.Copy(handles, 0, handlesMemory, count);
                Check(UpdateProcThreadAttribute(attribute, 0, (IntPtr)0x20002, handlesMemory,
                    (nuint)(count * IntPtr.Size), IntPtr.Zero, IntPtr.Zero));
                var startup = new StartupInfoEx { Info = new() { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
                    ReservedSize = (ushort)bytes, Reserved = crt, Input = handles[0], Output = handles[1], Error = handles[2] }, Attributes = attribute };
                var command = new StringBuilder(string.Join(" ", new[] { exe }.Concat(args).Select(Quote)));
                Check(CreateProcessW(exe, command, IntPtr.Zero, IntPtr.Zero, true, 0x08080000,
                    IntPtr.Zero, null, ref startup, out var result));
                CloseHandle(result.Thread);
                childHandle = new SafeProcessHandle(result.Process, true);
                // Keep non-inheritable duplicates to inspect native positions without resetting FileStream offsets.
                var monitoring = new List<SafeFileHandle>();
                try {
                    foreach (var input in inputHandles) {
                        Check(DuplicateHandle(GetCurrentProcess(), input, GetCurrentProcess(), out var monitor, 0, false, 2));
                        monitoring.Add(monitor);
                    }
                    var child = new BoundChild(childHandle, monitoring.ToArray(), stdout, stderr);
                    childHandle = null; stdout = null; stderr = null;
                    return child;
                }
                catch { foreach (var m in monitoring) m.Dispose(); throw; }
            }
            finally
            {
                if (childHandle is not null) { TerminateProcess(childHandle, 1); WaitForSingleObject(childHandle, 5000); childHandle.Dispose(); }
                foreach (var h in owned) h.Dispose();
                stdout?.Dispose(); stderr?.Dispose();
                if (initialized) DeleteProcThreadAttributeList(attribute);
                if (attribute != IntPtr.Zero) Marshal.FreeHGlobal(attribute);
                if (handlesMemory != IntPtr.Zero) Marshal.FreeHGlobal(handlesMemory);
                if (crt != IntPtr.Zero) Marshal.FreeHGlobal(crt);
            }
        }
        private static string Quote(string text)
        {
            if (text.Contains('\0')) throw new ArgumentException("NUL in process argument.");
            var result = new StringBuilder("\""); var slashes = 0;
            foreach (var c in text) {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        public void StopAndJoin()
        {
            if (!Exited && !TerminateProcess(process, 1) && !Exited) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (WaitForSingleObject(process, 5000) != 0) throw new IOException("Media process exit unconfirmed.");
        }
        public void Dispose() { StopAndJoin(); Output.Dispose(); Error.Dispose(); foreach (var s in sources) s.Dispose(); process.Dispose(); }
    }

    private static void Check(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastPInvokeError()); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo {
        public int Size; public IntPtr ReservedText, Desktop, Title; public int X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
        public ushort Show, ReservedSize; public IntPtr Reserved, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(IntPtr sourceProcess, SafeFileHandle source, IntPtr targetProcess, out SafeFileHandle target, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr security, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, nuint size, IntPtr previous, IntPtr resultSize);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string? directory, ref StartupInfoEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeProcessHandle process, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetFilePointerEx(SafeFileHandle file, long distance, out long position, uint origin);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
