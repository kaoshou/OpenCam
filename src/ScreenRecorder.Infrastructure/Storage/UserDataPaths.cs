// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Infrastructure.Storage;

public static class UserDataPaths
{
    public static string LocalApplicationData
    {
        get
        {
            // On Linux, None can return empty for a new user's absent XDG data
            // directory. Resolve the platform path without requiring it to exist;
            // the owning store/logger creates its own subdirectory afterward.
            var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                OperatingSystem.IsLinux() ? Environment.SpecialFolderOption.DoNotVerify : Environment.SpecialFolderOption.None);
            if (!Path.IsPathFullyQualified(path))
                throw new IOException("Cannot resolve an absolute user application-data directory.");
            return path;
        }
    }
}
