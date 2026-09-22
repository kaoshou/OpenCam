// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.Storage;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class StorageServiceTests : IDisposable
{
    private readonly string _tempDir;

    public StorageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StorageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void StorageService_DefaultPath_ShouldBeValid()
    {
        var service = new StorageService();
        var defaultPath = service.GetDefaultRecordingsPath();
        Assert.False(string.IsNullOrWhiteSpace(defaultPath));
        Assert.Contains("ScreenRecordings", defaultPath);
    }

    [Fact]
    public void StorageService_CreateSessionDirectory_ShouldCreateFolder()
    {
        var service = new StorageService();
        var sessionDir = service.CreateSessionDirectory(_tempDir, "session-123");

        Assert.True(Directory.Exists(sessionDir));
        Assert.Equal(Path.Combine(_tempDir, "Sessions", "session-123"), sessionDir);
    }

    [Fact]
    public void StorageService_GetFinalFilePath_ConflictResolution_ShouldAppendCounter()
    {
        var service = new StorageService();
        var timestamp = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

        var firstPath = service.GetFinalFilePath(_tempDir, timestamp);
        // 建立同名檔案以模擬衝突
        File.WriteAllText(firstPath, "dummy");

        var secondPath = service.GetFinalFilePath(_tempDir, timestamp);
        Assert.NotEqual(firstPath, secondPath);
        Assert.Contains("_1.mp4", secondPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }
}
