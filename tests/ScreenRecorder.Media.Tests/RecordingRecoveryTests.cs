using System.Diagnostics;
using ScreenRecorder.Core.Enums;
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
        var (recoverSuccess, errorMsg, recoveredMp4) = await recoveryService.RecoverSessionAsync(sessionDir);
        Assert.True(recoverSuccess, $"救援失敗: {errorMsg}");
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
}
