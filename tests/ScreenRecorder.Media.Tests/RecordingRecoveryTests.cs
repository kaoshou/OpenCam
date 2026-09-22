// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.Recovery;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Infrastructure.Storage;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Remux;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class RecordingRecoveryTests : IDisposable
{
    private readonly string _testRoot;

    public RecordingRecoveryTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "RecoveryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public async Task RecoveryService_ScanAndRecoverInterruptedSession_ShouldSucceed()
    {
        var sessionStore = new JsonRecordingSessionStore();
        var storageService = new StorageService();
        var remuxer = new StreamCopyRemuxer();
        var probe = new MediaFileProbe();

        var recoveryService = new RecordingRecoveryService(sessionStore, remuxer, probe, storageService);

        // 1. 模擬一個因當機或強制結束而中斷的 Session
        var sessionId = "crash-session-001";
        var sessionDir = storageService.CreateSessionDirectory(_testRoot, sessionId);
        var mkvPath = storageService.GetWorkingFilePath(sessionDir);

        // 生成 1 秒合法的 MKV 檔案代表崩潰前已寫入的資料
        var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable();
        Assert.NotNull(ffmpeg);
        using (var proc = Process.Start(new ProcessStartInfo
        {
            FileName = ffmpeg!,
            Arguments = $"-y -f lavfi -i testsrc=duration=1:size=320x240:rate=30 -c:v libx264 \"{mkvPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        }))
        {
            await proc!.WaitForExitAsync();
        }

        var interruptedSession = new RecordingSession
        {
            SessionId = sessionId,
            StartTime = DateTimeOffset.Now.AddMinutes(-5),
            State = RecordingState.Recording, // 未正常停止，保持在 Recording
            WorkingDirectory = sessionDir,
            WorkingFilePath = mkvPath,
            OutputWidth = 320,
            OutputHeight = 240
        };
        await sessionStore.SaveSessionAsync(interruptedSession);

        // 2. 執行掃描
        var recoverable = await recoveryService.ScanForRecoverableSessionsAsync(_testRoot);
        Assert.NotEmpty(recoverable);
        var target = recoverable.FirstOrDefault(s => s.Session.SessionId == sessionId);
        Assert.NotNull(target);
        Assert.True(target!.HasWorkingFile);
        Assert.True(target.WorkingFileSizeBytes > 0);

        // 3. 執行救援修復
        var (recoverSuccess, isPartial, errorMsg, recoveredMp4) = await recoveryService.RecoverSessionAsync(sessionDir);
        Assert.True(recoverSuccess, $"救援失敗: {errorMsg}");
        Assert.False(isPartial);
        Assert.NotNull(recoveredMp4);
        Assert.True(File.Exists(recoveredMp4));

        // 4. 原始 MKV 必須被保留（禁止刪除原始檔）
        Assert.True(File.Exists(mkvPath));

        // 5. 驗證 recovery.json 已產生
        Assert.True(File.Exists(Path.Combine(sessionDir, "recovery.json")));

        // 6. 驗證修復後的 MP4
        var probeResult = await probe.ProbeAsync(recoveredMp4!);
        Assert.True(probeResult.IsValid);
        Assert.Equal(320, probeResult.Width);
        Assert.Equal(240, probeResult.Height);
    }

    [Fact]
    public async Task ScanForRecoverableSessions_CurrentSegmentFormat_IsDetected()
    {
        var sessionStore = new JsonRecordingSessionStore();
        var storageService = new StorageService();
        var recoveryService = new RecordingRecoveryService(
            sessionStore,
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            storageService);
        var sessionId = "segment-session-001";
        var sessionDir = storageService.CreateSessionDirectory(_testRoot, sessionId);
        var firstSegment = Path.Combine(sessionDir, "segment_000.mkv");
        var secondSegment = Path.Combine(sessionDir, "segment_001.mkv");
        await File.WriteAllBytesAsync(firstSegment, new byte[128]);
        await File.WriteAllBytesAsync(secondSegment, new byte[256]);

        await sessionStore.SaveSessionAsync(new RecordingSession
        {
            SessionId = sessionId,
            StartTime = DateTimeOffset.Now.AddMinutes(-5),
            LastHeartbeatTime = DateTimeOffset.Now.AddMinutes(-5),
            State = RecordingState.Interrupted,
            WorkingDirectory = sessionDir,
            WorkingFilePath = secondSegment,
            SegmentFilePaths = new List<string> { firstSegment, secondSegment }
        });

        var recoverable = await recoveryService.ScanForRecoverableSessionsAsync(_testRoot);

        var target = Assert.Single(recoverable);
        Assert.Equal(sessionId, target.Session.SessionId);
        Assert.Equal(384, target.WorkingFileSizeBytes);
    }

    [Fact]
    public async Task ScanForRecoverableSessions_RecentHeartbeat_IsNotOfferedForRecovery()
    {
        var sessionStore = new JsonRecordingSessionStore();
        var storageService = new StorageService();
        var recoveryService = new RecordingRecoveryService(
            sessionStore,
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            storageService);
        var sessionId = "active-session-001";
        var sessionDir = storageService.CreateSessionDirectory(_testRoot, sessionId);
        var legacyWorkingFile = storageService.GetWorkingFilePath(sessionDir);
        await File.WriteAllBytesAsync(legacyWorkingFile, new byte[128]);

        await sessionStore.SaveSessionAsync(new RecordingSession
        {
            SessionId = sessionId,
            StartTime = DateTimeOffset.Now.AddMinutes(-1),
            LastHeartbeatTime = DateTimeOffset.Now,
            State = RecordingState.Recording,
            WorkingDirectory = sessionDir,
            WorkingFilePath = legacyWorkingFile
        });

        var recoverable = await recoveryService.ScanForRecoverableSessionsAsync(_testRoot);

        Assert.Empty(recoverable);
    }

    [Fact]
    public async Task ScanForRecoverableSessions_ImplausibleFutureHeartbeat_DoesNotStayActiveForever()
    {
        var sessionStore = new JsonRecordingSessionStore();
        var storageService = new StorageService();
        var recoveryService = new RecordingRecoveryService(
            sessionStore,
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            storageService);
        var sessionId = "future-heartbeat-session-001";
        var sessionDir = storageService.CreateSessionDirectory(_testRoot, sessionId);
        var workingFile = storageService.GetWorkingFilePath(sessionDir);
        await File.WriteAllBytesAsync(workingFile, new byte[128]);

        await sessionStore.SaveSessionAsync(new RecordingSession
        {
            SessionId = sessionId,
            StartTime = DateTimeOffset.Now.AddMinutes(-5),
            LastHeartbeatTime = DateTimeOffset.Now.AddHours(1),
            State = RecordingState.Recording,
            WorkingDirectory = sessionDir,
            WorkingFilePath = workingFile
        });

        var recoverable = await recoveryService.ScanForRecoverableSessionsAsync(_testRoot);

        Assert.Single(recoverable);
    }

    [Fact]
    public async Task ScanForRecoverableSessions_CorruptMetadata_ReconstructsOrphanAndKeepsScanning()
    {
        var sessionStore = new JsonRecordingSessionStore();
        var storageService = new StorageService();
        var recoveryService = new RecordingRecoveryService(
            sessionStore,
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            storageService);

        var validDirectory = storageService.CreateSessionDirectory(_testRoot, "valid-session");
        var validFile = storageService.GetWorkingFilePath(validDirectory);
        await File.WriteAllBytesAsync(validFile, new byte[128]);
        await sessionStore.SaveSessionAsync(new RecordingSession
        {
            SessionId = "valid-session",
            StartTime = DateTimeOffset.Now.AddMinutes(-5),
            State = RecordingState.Interrupted,
            WorkingDirectory = validDirectory,
            WorkingFilePath = validFile
        });

        var corruptDirectory = storageService.CreateSessionDirectory(_testRoot, "corrupt-session");
        await File.WriteAllTextAsync(Path.Combine(corruptDirectory, "session.json"), "{broken");
        var corruptSessionSegment = Path.Combine(corruptDirectory, "segment_000.mkv");
        var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable();
        Assert.NotNull(ffmpeg);
        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = ffmpeg!,
            Arguments = $"-y -f lavfi -i testsrc=duration=1:size=320x240:rate=30 -c:v libx264 \"{corruptSessionSegment}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        }))
        {
            await process!.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }

        var recoverable = await recoveryService.ScanForRecoverableSessionsAsync(_testRoot);

        Assert.Contains(recoverable, item => item.Session.SessionId == "valid-session");
        Assert.Contains(recoverable, item => item.Session.SessionId == "corrupt-session");

        var (success, isPartial, error, outputPath) =
            await recoveryService.RecoverSessionAsync(corruptDirectory);
        Assert.True(success, error);
        Assert.False(isPartial);
        Assert.NotNull(outputPath);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task RecoverSession_MissingMetadata_ReconstructsFromSegment()
    {
        var sessionStore = new JsonRecordingSessionStore();
        var storageService = new StorageService();
        var recoveryService = new RecordingRecoveryService(
            sessionStore,
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            storageService);
        var sessionDirectory = storageService.CreateSessionDirectory(
            _testRoot,
            "missing-metadata-session");
        var segment = Path.Combine(sessionDirectory, "segment_000.mkv");
        var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable();
        Assert.NotNull(ffmpeg);
        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = ffmpeg!,
            Arguments = $"-y -f lavfi -i testsrc=duration=1:size=320x240:rate=30 -c:v libx264 \"{segment}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        }))
        {
            await process!.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }

        var scan = await recoveryService.ScanForRecoverableSessionsAsync(_testRoot);
        Assert.Contains(
            scan,
            item => item.Session.SessionId == "missing-metadata-session");

        var (success, isPartial, error, outputPath) =
            await recoveryService.RecoverSessionAsync(sessionDirectory);
        Assert.True(success, error);
        Assert.False(isPartial);
        Assert.NotNull(outputPath);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task RecoverSession_CorruptTrailingSegment_RecoversEarlierValidSegments()
    {
        var sessionStore = new JsonRecordingSessionStore();
        var storageService = new StorageService();
        var probe = new MediaFileProbe();
        var recoveryService = new RecordingRecoveryService(
            sessionStore,
            new FallbackRequiredRemuxer(),
            probe,
            storageService);
        var sessionId = "partial-session-001";
        var sessionDir = storageService.CreateSessionDirectory(_testRoot, sessionId);
        var validSegment = Path.Combine(sessionDir, "segment_000.mkv");
        var corruptSegment = Path.Combine(sessionDir, "segment_001.mkv");

        var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable();
        Assert.NotNull(ffmpeg);
        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = ffmpeg!,
            Arguments = $"-y -f lavfi -i testsrc=duration=1:size=320x240:rate=30 -c:v libx264 \"{validSegment}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        }))
        {
            await process!.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        await File.WriteAllBytesAsync(corruptSegment, new byte[1024]);

        await sessionStore.SaveSessionAsync(new RecordingSession
        {
            SessionId = sessionId,
            StartTime = DateTimeOffset.Now.AddMinutes(-5),
            LastHeartbeatTime = DateTimeOffset.Now.AddMinutes(-5),
            State = RecordingState.Interrupted,
            WorkingDirectory = sessionDir,
            WorkingFilePath = corruptSegment,
            SegmentFilePaths = new List<string> { validSegment, corruptSegment }
        });

        var (success, isPartial, error, outputPath) = await recoveryService.RecoverSessionAsync(sessionDir);

        Assert.True(success, error);
        Assert.True(isPartial);
        Assert.NotNull(outputPath);
        var recovered = await probe.ProbeAsync(outputPath!);
        Assert.True(recovered.IsValid);
        Assert.True(recovered.VideoStreamCount > 0);
        Assert.True(File.Exists(validSegment));
        Assert.True(File.Exists(corruptSegment));
    }

    [Fact]
    public async Task RecoverSession_ZeroByteTrailingSegment_IsReportedAsPartial()
    {
        var sessionStore = new JsonRecordingSessionStore();
        var storageService = new StorageService();
        var recoveryService = new RecordingRecoveryService(
            sessionStore,
            new StreamCopyRemuxer(),
            new MediaFileProbe(),
            storageService);
        var sessionDirectory = storageService.CreateSessionDirectory(
            _testRoot,
            "zero-tail-session");
        var validSegment = Path.Combine(sessionDirectory, "segment_000.mkv");
        var emptySegment = Path.Combine(sessionDirectory, "segment_001.mkv");
        var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable();
        Assert.NotNull(ffmpeg);
        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = ffmpeg!,
            Arguments = $"-y -f lavfi -i testsrc=duration=1:size=320x240:rate=30 -c:v libx264 \"{validSegment}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        }))
        {
            await process!.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        await File.WriteAllBytesAsync(emptySegment, Array.Empty<byte>());
        await sessionStore.SaveSessionAsync(new RecordingSession
        {
            SessionId = "zero-tail-session",
            StartTime = DateTimeOffset.Now.AddMinutes(-5),
            LastHeartbeatTime = DateTimeOffset.Now.AddMinutes(-5),
            State = RecordingState.Interrupted,
            WorkingDirectory = sessionDirectory,
            WorkingFilePath = emptySegment,
            SegmentFilePaths = new List<string> { validSegment, emptySegment }
        });

        var (success, isPartial, error, outputPath) =
            await recoveryService.RecoverSessionAsync(sessionDirectory);

        Assert.True(success, error);
        Assert.True(isPartial);
        Assert.NotNull(outputPath);
        Assert.True(File.Exists(emptySegment));
        Assert.Equal(0, new FileInfo(emptySegment).Length);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch { }
    }

    private sealed class FallbackRequiredRemuxer : IStreamCopyRemuxer
    {
        private readonly StreamCopyRemuxer _inner = new();

        public Task<bool> RemuxToMp4Async(
            string mkvInputPath,
            string mp4OutputPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            _inner.RemuxToMp4Async(
                mkvInputPath,
                mp4OutputPath,
                progress,
                cancellationToken);

        public Task<bool> ConcatAndRemuxToMp4Async(
            IReadOnlyList<string> mkvInputPaths,
            string mp4OutputPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
