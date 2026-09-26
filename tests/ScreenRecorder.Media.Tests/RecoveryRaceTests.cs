// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.Recovery;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Infrastructure.Storage;

namespace ScreenRecorder.Media.Tests;

public sealed class RecoveryRaceTests
{
    [Fact]
    public async Task HardLinkedLockReturnsFailureWithoutChangingTarget()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-lock-test-").FullName;
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "Sessions", "session")).FullName;
            var source = Path.Combine(dir, "recording.mkv");
            await File.WriteAllTextAsync(source, "authorized-media");
            var sentinel = Path.Combine(root, "sentinel");
            await File.WriteAllTextAsync(sentinel, "preserve");
            var store = new JsonRecordingSessionStore();
            await store.SaveSessionAsync(new RecordingSession
            {
                SessionId = "session", WorkingDirectory = dir, WorkingFilePath = source,
                State = RecordingState.Interrupted
            });
            var lockPath = Path.Combine(dir, ".recovery.lock");
            Assert.True(OperatingSystem.IsWindows()
                ? CreateHardLink(lockPath, sentinel, IntPtr.Zero)
                : link(sentinel, lockPath) == 0);
            var recovery = new RecordingRecoveryService(store, new CopyingRemuxer(), new ReplacingProbe(() => { }), new StorageService());
            var result = await recovery.RecoverSessionAsync(dir);
            Assert.False(result.Success);
            Assert.Equal("preserve", await File.ReadAllTextAsync(sentinel));
        }
        finally { Directory.Delete(root, true); }
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int link(string existing, string path);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string path, string existing, IntPtr security);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ReplacingSessionDirectoryDuringProbeCannotWriteIntoReplacement(int ancestor)
    {
        var fixture = Directory.CreateTempSubdirectory("OpenCam-directory-race-").FullName;
        var root = Path.Combine(fixture, "recordings");
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "Sessions", "session")).FullName;
            var outside = Directory.CreateDirectory(Path.Combine(fixture, "outside")).FullName;
            var source = Path.Combine(dir, "recording.mkv");
            await File.WriteAllTextAsync(source, "authorized-media");
            await File.WriteAllTextAsync(Path.Combine(outside, "recovery.json"), "preserve");
            var store = new JsonRecordingSessionStore();
            await store.SaveSessionAsync(new RecordingSession
            {
                SessionId = "session", WorkingDirectory = dir, WorkingFilePath = source,
                State = RecordingState.Interrupted
            });
            var probe = new ReplacingProbe(() =>
            {
                try
                {
                    var target = ancestor == 0 ? dir : ancestor == 1 ? Path.GetDirectoryName(dir)! : root;
                    Directory.Move(target, target + "-original");
                    Directory.CreateSymbolicLink(target, outside);
                }
                catch (IOException) when (OperatingSystem.IsWindows()) { /* directory lease blocks rename */ }
            });
            var recovery = new RecordingRecoveryService(store, new CopyingRemuxer(), probe, new StorageService());
            var result = await recovery.RecoverSessionAsync(dir);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(File.Exists(result.FinalMp4Path));
            Assert.Equal("preserve", await File.ReadAllTextAsync(Path.Combine(outside, "recovery.json")));
            Assert.False(File.Exists(Path.Combine(outside, "session.json")));
            Assert.Empty(Directory.EnumerateFiles(outside, "*.mp4"));
        }
        finally { Directory.Delete(fixture, true); }
    }

    [Fact]
    public async Task ReplacingSourceAndMetadataDuringProbeCannotRedirectRecovery()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-recovery-race-").FullName;
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "Sessions", "session")).FullName;
            var source = Path.Combine(dir, "recording.mkv");
            var outside = Path.Combine(root, "outside.mkv");
            var sentinel = Path.Combine(root, "sentinel.txt");
            await File.WriteAllTextAsync(source, "authorized-media");
            await File.WriteAllTextAsync(outside, "external-media");
            await File.WriteAllTextAsync(sentinel, "preserve");
            var store = new JsonRecordingSessionStore();
            await store.SaveSessionAsync(new RecordingSession
            {
                SessionId = "session", WorkingDirectory = dir, WorkingFilePath = source,
                State = RecordingState.Interrupted
            });
            var probe = new ReplacingProbe(() =>
            {
                File.Delete(source);
                File.CreateSymbolicLink(source, outside);
                File.CreateSymbolicLink(Path.Combine(dir, "recovery.json"), sentinel);
            });
            var recovery = new RecordingRecoveryService(store, new CopyingRemuxer(), probe, new StorageService());
            var result = await recovery.RecoverSessionAsync(dir);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("authorized-media", await File.ReadAllTextAsync(result.FinalMp4Path!));
            Assert.Equal("preserve", await File.ReadAllTextAsync(sentinel));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ReplacingProbe(Action replace) : IMediaProbeService
    {
        private bool _replaced;
        public Task<MediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken = default)
        {
            if (!_replaced) { _replaced = true; replace(); }
            return Task.FromResult(new MediaProbeResult(true, "test", TimeSpan.FromSeconds(1),
                16, 1, 0, "h264", 320, 240, 30, null, 0, null));
        }
    }

    private sealed class CopyingRemuxer : IStreamCopyRemuxer
    {
        public Task<bool> RemuxToMp4Async(string input, string output, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            File.Copy(input, output, true);
            return Task.FromResult(true);
        }
        public Task<bool> ConcatAndRemuxToMp4Async(IReadOnlyList<string> inputs, string output, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            RemuxToMp4Async(inputs[0], output, progress, cancellationToken);
    }
}
