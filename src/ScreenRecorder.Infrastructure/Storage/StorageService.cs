// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Infrastructure.Storage;

public class StorageService : IStorageService
{
    public string GetDefaultRecordingsPath()
    {
        var videosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videosPath))
        {
            videosPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
        }
        return Path.Combine(videosPath, "ScreenRecordings");
    }

    public string CreateSessionDirectory(string rootPath, string sessionId)
    {
        var sessionDir = Path.Combine(rootPath, "Sessions", sessionId);
        Directory.CreateDirectory(sessionDir);
        return sessionDir;
    }

    public string GetWorkingFilePath(string sessionDirectory)
    {
        return Path.Combine(sessionDirectory, "recording.mkv");
    }

    public string GetFinalFilePath(string rootPath, DateTimeOffset timestamp)
    {
        Directory.CreateDirectory(rootPath);
        var fileName = $"Recording_{timestamp:yyyyMMdd_HHmmss}.mp4";
        var finalPath = Path.Combine(rootPath, fileName);

        // 若發生檔名衝突，自動附加遞增序號
        int counter = 1;
        while (File.Exists(finalPath))
        {
            fileName = $"Recording_{timestamp:yyyyMMdd_HHmmss}_{counter}.mp4";
            finalPath = Path.Combine(rootPath, fileName);
            counter++;
        }

        return finalPath;
    }

    public string GetSessionLogFilePath(string sessionDirectory)
    {
        return Path.Combine(sessionDirectory, "recording.log");
    }

    public long GetAvailableFreeSpaceBytes(string directoryPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(directoryPath);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                return 0;
            }

            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    public bool IsDiskSpaceSufficient(string directoryPath, long requiredBytes)
    {
        return GetAvailableFreeSpaceBytes(directoryPath) >= requiredBytes;
    }
}
