// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.Session;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class SessionStoreTests : IDisposable
{
    private readonly string _testRoot;

    public SessionStoreTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "ScreenRecorderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public async Task SessionStore_SaveAndLoad_ShouldPreserveData()
    {
        var store = new JsonRecordingSessionStore();
        var sessionDir = Path.Combine(_testRoot, "Session1");

        var session = new RecordingSession
        {
            SessionId = "test-session-001",
            StartTime = DateTimeOffset.UtcNow,
            LastHeartbeatTime = DateTimeOffset.UtcNow,
            State = RecordingState.Recording,
            WorkingDirectory = sessionDir,
            WorkingFilePath = Path.Combine(sessionDir, "recording.mkv"),
            FinalFilePath = Path.Combine(_testRoot, "Recording_final.mp4"),
            OutputWidth = 1920,
            OutputHeight = 1080,
            TotalVideoFramesRecorded = 300,
            Configuration = new RecordingConfiguration
            {
                Fps = 60,
                AudioSource = AudioSourceType.SystemAndMicrophone
            }
        };

        await store.SaveSessionAsync(session);

        var loaded = await store.LoadSessionAsync(sessionDir);

        Assert.NotNull(loaded);
        Assert.Equal("test-session-001", loaded!.SessionId);
        Assert.Equal(RecordingState.Recording, loaded.State);
        Assert.Equal(1920, loaded.OutputWidth);
        Assert.Equal(60, loaded.Configuration.Fps);
        Assert.Equal(300, loaded.TotalVideoFramesRecorded);

        // 驗證備份檔案是否已正確產生
        var backupPath = Path.Combine(sessionDir, "session.json.bak");
        // 再次儲存更新，此時應觸發備份機制
        session.TotalVideoFramesRecorded = 600;
        await store.SaveSessionAsync(session);

        Assert.True(File.Exists(backupPath));
        var loadedUpdated = await store.LoadSessionAsync(sessionDir);
        Assert.Equal(600, loadedUpdated!.TotalVideoFramesRecorded);
    }

    [Fact]
    public async Task SessionStore_CorruptedMainFile_ShouldFallbackToBackup()
    {
        var store = new JsonRecordingSessionStore();
        var sessionDir = Path.Combine(_testRoot, "Session2");

        var session = new RecordingSession
        {
            SessionId = "test-session-corrupt",
            StartTime = DateTimeOffset.UtcNow,
            State = RecordingState.Interrupted,
            WorkingDirectory = sessionDir
        };

        // 第一次儲存
        await store.SaveSessionAsync(session);

        // 第二次儲存以產生備份檔
        session.TotalVideoFramesRecorded = 123;
        await store.SaveSessionAsync(session);

        // 人為破壞主要 session.json 檔案
        var mainFilePath = Path.Combine(sessionDir, "session.json");
        await File.WriteAllTextAsync(mainFilePath, "INVALID JSON CONTENT {{{{");

        // 載入時應自動降級由 session.json.bak 還原成功
        var recovered = await store.LoadSessionAsync(sessionDir);
        Assert.NotNull(recovered);
        Assert.Equal("test-session-corrupt", recovered!.SessionId);
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
