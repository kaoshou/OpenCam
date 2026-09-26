// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ScreenRecorder.Infrastructure.Recovery;

namespace ScreenRecorder.Infrastructure.Session;

/// <summary>Operations are bound to opened directories, never re-resolved through mutable ancestors.</summary>
public sealed class BoundDirectory : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    private SafeFileHandle Handle => _handles[^1];
    public string PathName { get; }
    public string CurrentPath
    {
        get
        {
            if (OperatingSystem.IsWindows()) return PathName;
            if (OperatingSystem.IsMacOS())
            {
                var path = new byte[1024];
                var result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? FcntlMacArm64(Handle, 50, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, path)
                    : fcntl(Handle, 50, path);
                if (result != 0) throw new IOException("Cannot locate pinned recording directory.");
                return Encoding.UTF8.GetString(path, 0, Array.IndexOf(path, (byte)0));
            }
            return new FileInfo($"/proc/self/fd/{Handle.DangerousGetHandle().ToInt32()}").LinkTarget
                ?? throw new IOException("Cannot locate pinned recording directory.");
        }
    }
    private static int DirectoryFlags => OperatingSystem.IsMacOS() ? 0x100000 | 0x100 | 0x1000000 : 0x10000 | 0x20000 | 0x80000;
    private static int NoFollowFlags => OperatingSystem.IsMacOS() ? 0x100 | 0x4 | 0x1000000 : 0x20000 | 0x800 | 0x80000;

    private BoundDirectory(string path) => PathName = path;

    public static BoundDirectory Open(string path, bool create = false)
    {
        var full = Path.GetFullPath(path);
        // macOS system aliases, not user-controlled recording-directory links.
        if (OperatingSystem.IsMacOS())
        {
            if (full.StartsWith("/var/", StringComparison.Ordinal) || full.StartsWith("/tmp/", StringComparison.Ordinal))
                full = "/private" + full;
        }
        var result = new BoundDirectory(Path.TrimEndingDirectorySeparator(full));
        try
        {
            var root = Path.GetPathRoot(full)!;
            var current = root;
            result._handles.Add(OpenDirectory(root, null));
            foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (create)
                {
                    if (OperatingSystem.IsWindows()) Directory.CreateDirectory(current);
                    else if (mkdirat(result.Handle, part, 0x1C0) != 0 && Marshal.GetLastWin32Error() != 17)
                        throw new IOException("Cannot create recording directory.");
                }
                result._handles.Add(OpenDirectory(OperatingSystem.IsWindows() ? current : part, result.Handle));
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static SafeFileHandle OpenDirectory(string path, SafeFileHandle? parent)
    {
        if (OperatingSystem.IsWindows())
        {
            var handle = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) ||
                (info.Attributes & 0x400) != 0 || (info.Attributes & 0x10) == 0)
            { handle.Dispose(); throw new IOException("Linked or inaccessible recording directory."); }
            return handle; // no FILE_SHARE_DELETE: pins the entire ancestor chain
        }
        var fd = parent == null ? open(path, DirectoryFlags, 0) : openat(parent, path, DirectoryFlags, 0);
        if (fd < 0) throw new IOException("Linked or inaccessible recording directory.");
        return new SafeFileHandle((IntPtr)fd, true);
    }

    private static void Leaf(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', ':', '\0']) >= 0)
            throw new InvalidDataException("Expected a single file name.");
    }

    public FileStream Read(string name)
    {
        Leaf(name);
        if (OperatingSystem.IsWindows()) return RecoverySourceFile.Open(Path.Combine(PathName, name));
        var fd = openat(Handle, name, NoFollowFlags, 0);
        if (fd < 0)
        {
            if (Marshal.GetLastWin32Error() == 2) throw new FileNotFoundException("Session file does not exist.", name);
            throw new IOException("Cannot safely read session file.");
        }
        return RecoverySourceFile.FromUnixHandle(new SafeFileHandle((IntPtr)fd, true));
    }

    private FileStream Create(string name, bool exclusive)
    {
        Leaf(name);
        if (OperatingSystem.IsWindows())
            return new FileStream(Path.Combine(PathName, name), exclusive ? FileMode.CreateNew : FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        var flags = NoFollowFlags | 2 | (OperatingSystem.IsMacOS() ? 0x200 : 0x40);
        if (exclusive) flags |= OperatingSystem.IsMacOS() ? 0x800 : 0x80;
        // Darwin arm64 passes the variadic mode argument on the stack rather
        // than in the fourth integer register. Use an explicit ABI bridge.
        var fd = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? OpenAtMacArm64(Handle, name, flags, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x180)
            : openat(Handle, name, flags, 0x180);
        if (fd < 0) throw new IOException("Cannot safely create session file.");
        return new FileStream(new SafeFileHandle((IntPtr)fd, true), FileAccess.ReadWrite);
    }

    public FileStream Claim()
    {
        // Existing lock contents are never read or truncated. Reject links before
        // opening on Windows while the ancestor lease prevents parent replacement.
        if (OperatingSystem.IsWindows())
        {
            var handle = CreateFile(Path.Combine(PathName, ".recovery.lock"), 0xC0000000, 0, IntPtr.Zero, 4, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) ||
                (info.Attributes & 0x410) != 0 || info.Links != 1)
            { handle.Dispose(); throw new IOException("Unsafe or busy recovery lock."); }
            return new FileStream(handle, FileAccess.ReadWrite);
        }
        var stream = Create(".recovery.lock", false);
        try
        {
            RecoverySourceFile.ValidateUnixHandle(stream.SafeFileHandle);
            if (flock(stream.SafeFileHandle, 2 | 4) != 0) throw new IOException("Recovery already in progress.");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    public async Task<string> ReadTextAsync(string name, CancellationToken token)
    {
        using var stream = Read(name);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Session metadata exceeds 4 MiB.");
        using var reader = new StreamReader(stream);
        var buffer = new char[4096];
        var text = new StringBuilder();
        int length;
        while ((length = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (text.Length + length > 4 * 1024 * 1024) throw new InvalidDataException("Session metadata exceeds limit.");
            text.Append(buffer, 0, length);
        }
        return text.ToString();
    }

    public async Task WriteTextAsync(string name, string content, CancellationToken token)
    {
        Leaf(name);
        var temporary = $".opencam-{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = Create(temporary, true))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(content), token);
                stream.Flush(flushToDisk: true);
            }
            if (OperatingSystem.IsWindows()) File.Move(Path.Combine(PathName, temporary), Path.Combine(PathName, name), true);
            else if (renameat(Handle, temporary, Handle, name) != 0) throw new IOException("Cannot replace session metadata.");
        }
        finally { DeleteLeaf(temporary); }
    }

    public async Task<string> PublishAsync(string stagedFile, string name, CancellationToken token)
    {
        Leaf(name);
        // Exclusive creation at the pinned destination supports different volumes
        // without any overwrite window. Keep a partial failure out of the results.
        var created = false;
        try
        {
            await using var output = Create(name, true);
            created = true;
            await using var input = File.OpenRead(stagedFile);
            await input.CopyToAsync(output, token);
            output.Flush(flushToDisk: true);
            return Path.Combine(CurrentPath, name);
        }
        catch { if (created) DeleteLeaf(name); throw; }
    }

    private void DeleteLeaf(string name)
    {
        if (OperatingSystem.IsWindows()) File.Delete(Path.Combine(PathName, name));
        else unlinkat(Handle, name, 0); // deletes the entry, never follows a link
    }

    public void Dispose() { for (var i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose(); }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfoNative { public uint Attributes, C1, C2, A1, A2, W1, W2, Volume, SizeHigh, SizeLow, Links, I1, I2; }
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags, uint mode);
    [DllImport("libc", SetLastError = true)] private static extern int openat(SafeFileHandle dir, string path, int flags, uint mode);
    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAtMacArm64(SafeFileHandle dir, string path, int flags,
        IntPtr a, IntPtr b, IntPtr c, IntPtr d, IntPtr e, uint mode);
    [DllImport("libc", SetLastError = true)] private static extern int mkdirat(SafeFileHandle dir, string path, uint mode);
    [DllImport("libc", SetLastError = true)] private static extern int renameat(SafeFileHandle from, string oldName, SafeFileHandle to, string newName);
    [DllImport("libc", SetLastError = true)] private static extern int unlinkat(SafeFileHandle dir, string name, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int flock(SafeFileHandle handle, int operation);
    [DllImport("libc", SetLastError = true)] private static extern int fcntl(SafeFileHandle handle, int command, [Out] byte[] path);
    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int FcntlMacArm64(SafeFileHandle handle, int command,
        IntPtr a, IntPtr b, IntPtr c, IntPtr d, IntPtr e, IntPtr f, [Out] byte[] path);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
}
