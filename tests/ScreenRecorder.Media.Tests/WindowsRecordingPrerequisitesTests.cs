// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Media.Capture;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class WindowsRecordingPrerequisitesTests
{
    [Fact]
    public void MissingRuntimeExplainsRequiredInstallerWithoutStartingCapture()
    {
        var error = Assert.Throws<IOException>(() => WindowsRecordingPrerequisites.Validate(_ => false));
        Assert.Contains("Visual C++", error.Message);
        Assert.Contains("vcruntime140.dll", error.Message);
        Assert.Contains("https://aka.ms/vc14/vc_redist.x64.exe", error.Message);
    }

    [Fact]
    public void CompleteSystemRuntimeAllowsCaptureToProceed()
    {
        var checkedFiles = new List<string>();
        WindowsRecordingPrerequisites.Validate(name => { checkedFiles.Add(name); return true; });
        Assert.Equal(new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll" }, checkedFiles);
    }
}
