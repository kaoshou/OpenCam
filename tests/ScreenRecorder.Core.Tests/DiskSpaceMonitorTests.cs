// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Infrastructure.Diagnostics;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class MockStorageService : IStorageService
{
    public long SimulatedFreeSpace { get; set; } = 10L * 1024 * 1024 * 1024; // 10 GB
    public Action? SpaceRead { get; set; }

    public string CreateSessionDirectory(string rootPath, string sessionId) => string.Empty;
    public string GetDefaultRecordingsPath() => string.Empty;
    public string GetFinalFilePath(string rootPath, DateTimeOffset timestamp) => string.Empty;
    public string GetSessionLogFilePath(string sessionDirectory) => string.Empty;
    public string GetWorkingFilePath(string sessionDirectory) => string.Empty;
    public bool IsDiskSpaceSufficient(string directoryPath, long requiredBytes) => SimulatedFreeSpace >= requiredBytes;
    public long GetAvailableFreeSpaceBytes(string directoryPath)
    {
        SpaceRead?.Invoke();
        return SimulatedFreeSpace;
    }
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

        var warning = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.DiskSpaceWarningTriggered += (s, space) => warning.TrySetResult(space);

        // 設定可用空間為 1 GB (低於 2GB 警告門檻，高於 500MB 臨界門檻)
        mockStorage.SimulatedFreeSpace = 1L * 1024 * 1024 * 1024;

        monitor.StartMonitoring("C:\\", TimeSpan.FromMilliseconds(50));
        var reportedSpace = await warning.Task.WaitAsync(TimeSpan.FromSeconds(5));
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

        var critical = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.DiskSpaceCriticalTriggered += (s, space) => critical.TrySetResult(space);

        // 設定可用空間為 200 MB (低於 500MB 臨界門檻)
        mockStorage.SimulatedFreeSpace = 200L * 1024 * 1024;

        monitor.StartMonitoring("C:\\", TimeSpan.FromMilliseconds(50));
        var reportedSpace = await critical.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
        int readCount = 0;
        var warning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var polls = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.DiskSpaceWarningTriggered += (s, space) =>
        {
            Interlocked.Increment(ref triggerCount);
            warning.TrySetResult();
        };
        mockStorage.SpaceRead = () =>
        {
            if (Interlocked.Increment(ref readCount) >= 6) polls.TrySetResult();
        };

        // 設定可用空間為 1 GB (觸發 warning)
        mockStorage.SimulatedFreeSpace = 1L * 1024 * 1024 * 1024;

        // Wait for actual polling and delivery, not an assumed CI scheduling speed.
        monitor.StartMonitoring("C:\\", TimeSpan.FromMilliseconds(20));
        await Task.WhenAll(warning.Task, polls.Task).WaitAsync(TimeSpan.FromSeconds(5));

        // 驗證防抖機制：即便 tick 多次，Warning 事件依然只觸發 1 次！
        Assert.Equal(1, Volatile.Read(ref triggerCount));
    }
}
