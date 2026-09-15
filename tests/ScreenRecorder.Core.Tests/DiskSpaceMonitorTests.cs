using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Infrastructure.Diagnostics;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class MockStorageService : IStorageService
{
    public long SimulatedFreeSpace { get; set; } = 10L * 1024 * 1024 * 1024; // 10 GB

    public string CreateSessionDirectory(string rootPath, string sessionId) => string.Empty;
    public string GetDefaultRecordingsPath() => string.Empty;
    public string GetFinalFilePath(string rootPath, DateTimeOffset timestamp) => string.Empty;
    public string GetSessionLogFilePath(string sessionDirectory) => string.Empty;
    public string GetWorkingFilePath(string sessionDirectory) => string.Empty;
    public bool IsDiskSpaceSufficient(string directoryPath, long requiredBytes) => SimulatedFreeSpace >= requiredBytes;
    public long GetAvailableFreeSpaceBytes(string directoryPath) => SimulatedFreeSpace;
}

public class DiskSpaceMonitorTests
{
    [Fact]
    public async Task DiskSpaceMonitor_WarningThreshold_ShouldTriggerWarningEvent()
    {
        var mockStorage = new MockStorageService();
        using var monitor = new DiskSpaceMonitor(mockStorage)
        {
            WarningThresholdBytes = 2L * 1024 * 1024 * 1024,
            CriticalThresholdBytes = 500L * 1024 * 1024
        };

        bool warningTriggered = false;
        long reportedSpace = 0;
        monitor.DiskSpaceWarningTriggered += (s, space) =>
        {
            warningTriggered = true;
            reportedSpace = space;
        };

        // 設定可用空間為 1 GB (低於 2GB 警告門檻，高於 500MB 臨界門檻)
        mockStorage.SimulatedFreeSpace = 1L * 1024 * 1024 * 1024;

        monitor.StartMonitoring("C:\\", TimeSpan.FromMilliseconds(50));
        await Task.Delay(150);

        Assert.True(warningTriggered);
        Assert.Equal(1L * 1024 * 1024 * 1024, reportedSpace);
    }

    [Fact]
    public async Task DiskSpaceMonitor_CriticalThreshold_ShouldTriggerCriticalEvent()
    {
        var mockStorage = new MockStorageService();
        using var monitor = new DiskSpaceMonitor(mockStorage)
        {
            WarningThresholdBytes = 2L * 1024 * 1024 * 1024,
            CriticalThresholdBytes = 500L * 1024 * 1024
        };

        bool criticalTriggered = false;
        long reportedSpace = 0;
        monitor.DiskSpaceCriticalTriggered += (s, space) =>
        {
            criticalTriggered = true;
            reportedSpace = space;
        };

        // 設定可用空間為 200 MB (低於 500MB 臨界門檻)
        mockStorage.SimulatedFreeSpace = 200L * 1024 * 1024;

        monitor.StartMonitoring("C:\\", TimeSpan.FromMilliseconds(50));
        await Task.Delay(150);

        Assert.True(criticalTriggered);
        Assert.Equal(200L * 1024 * 1024, reportedSpace);
    }

    [Fact]
    public async Task DiskSpaceMonitor_Debounce_ShouldTriggerWarningOnlyOnceWhenRemainingLow()
    {
        var mockStorage = new MockStorageService();
        using var monitor = new DiskSpaceMonitor(mockStorage)
        {
            WarningThresholdBytes = 2L * 1024 * 1024 * 1024,
            CriticalThresholdBytes = 500L * 1024 * 1024
        };

        int triggerCount = 0;
        monitor.DiskSpaceWarningTriggered += (s, space) => triggerCount++;

        // 設定可用空間為 1 GB (觸發 warning)
        mockStorage.SimulatedFreeSpace = 1L * 1024 * 1024 * 1024;

        // 輪詢週期極短 (20ms)，在 120ms 內預期會 tick 5-6 次
        monitor.StartMonitoring("C:\\", TimeSpan.FromMilliseconds(20));
        await Task.Delay(120);

        // 驗證防抖機制：即便 tick 多次，Warning 事件依然只觸發 1 次！
        Assert.Equal(1, triggerCount);
    }
}
