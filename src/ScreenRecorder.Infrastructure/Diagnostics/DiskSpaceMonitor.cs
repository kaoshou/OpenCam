using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Infrastructure.Diagnostics;

public class DiskSpaceMonitor : IDiskSpaceMonitor
{
    private readonly IStorageService _storageService;
    private Timer? _timer;
    private string? _targetDirectory;
    private bool _isDisposed;
    private bool _hasWarned;
    private readonly object _lock = new();

    public long WarningThresholdBytes { get; set; } = 2L * 1024 * 1024 * 1024; // 2 GB
    public long CriticalThresholdBytes { get; set; } = 500L * 1024 * 1024;     // 500 MB

    public event EventHandler<long>? DiskSpaceWarningTriggered;
    public event EventHandler<long>? DiskSpaceCriticalTriggered;

    public DiskSpaceMonitor(IStorageService storageService)
    {
        _storageService = storageService;
    }

    public void StartMonitoring(string targetDirectoryPath, TimeSpan interval)
    {
        lock (_lock)
        {
            _targetDirectory = targetDirectoryPath;
            _hasWarned = false;
            _timer?.Dispose();
            _timer = new Timer(CheckSpaceCallback, null, TimeSpan.Zero, interval);
        }
    }

    public void StopMonitoring()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _hasWarned = false;
        }
    }

    private void CheckSpaceCallback(object? state)
    {
        string? target;
        lock (_lock)
        {
            target = _targetDirectory;
        }

        if (string.IsNullOrEmpty(target)) return;

        var freeSpace = _storageService.GetAvailableFreeSpaceBytes(target);

        if (freeSpace <= CriticalThresholdBytes)
        {
            DiskSpaceCriticalTriggered?.Invoke(this, freeSpace);
        }
        else if (freeSpace <= WarningThresholdBytes)
        {
            bool shouldWarn = false;
            lock (_lock)
            {
                if (!_hasWarned)
                {
                    _hasWarned = true;
                    shouldWarn = true;
                }
            }

            if (shouldWarn)
            {
                DiskSpaceWarningTriggered?.Invoke(this, freeSpace);
            }
        }
        else
        {
            lock (_lock)
            {
                _hasWarned = false;
            }
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            StopMonitoring();
        }
    }
}
