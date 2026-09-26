// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Media.Tests;

internal static class RepositoryTestFiles
{
    public static string Find(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ScreenRecorder.sln")))
                return Path.Combine(directory.FullName, relativePath);
        throw new DirectoryNotFoundException("Cannot find the repository containing the test sources.");
    }
}
