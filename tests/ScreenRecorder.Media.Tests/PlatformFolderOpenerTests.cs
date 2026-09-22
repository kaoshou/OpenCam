// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.UI.Services;

namespace ScreenRecorder.Media.Tests;

public class PlatformFolderOpenerTests : IDisposable
{
    private readonly string _directory;
    private readonly string _file;

    public PlatformFolderOpenerTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "OpenCam Folder " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _file = Path.Combine(_directory, "My Recording.mp4");
        File.WriteAllText(_file, "fixture");
    }

    [Fact]
    public void MacFile_UsesFinderRevealWithOnePathArgument()
    {
        var info = PlatformFolderOpener.BuildStartInfo(
            isWindows: false,
            isMacOS: true,
            _file,
            _directory);

        Assert.Equal("/usr/bin/open", info!.FileName);
        Assert.Equal(new[] { "-R", _file }, info.ArgumentList);
        Assert.False(info.UseShellExecute);
    }

    [Fact]
    public void MacDirectory_UsesOpen()
    {
        var info = PlatformFolderOpener.BuildStartInfo(
            isWindows: false,
            isMacOS: true,
            lastOutputFilePath: null,
            _directory);

        Assert.Equal("/usr/bin/open", info!.FileName);
        Assert.Equal(new[] { _directory }, info.ArgumentList);
    }

    [Fact]
    public void WindowsFile_PreservesExplorerSelection()
    {
        var info = PlatformFolderOpener.BuildStartInfo(
            isWindows: true,
            isMacOS: false,
            _file,
            _directory);

        Assert.Equal("explorer.exe", info!.FileName);
        Assert.Equal(new[] { "/select," + _file }, info.ArgumentList);
        Assert.False(info.UseShellExecute);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
