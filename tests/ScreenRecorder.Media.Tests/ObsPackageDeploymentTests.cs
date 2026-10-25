// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Media.Tests;

public sealed class ObsPackageDeploymentTests
{
    [EnabledObsPackageFact]
    public void UserTestPackageIsAcceptedByTheProductionRuntimeLoader()
    {
        var root = Environment.GetEnvironmentVariable("OPENCAM_OBS_ENABLED_RUNTIME")!;
        var runtime = ObsRuntime.TryLoad(root);
        Assert.NotNull(runtime);
        Assert.Equal("win-x64", runtime.Platform);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "OpenCam.ObsCapture.exe"), runtime.HelperPath);
        Assert.Contains("wgc", runtime.Backends);
    }
}

public sealed class EnabledObsPackageFactAttribute : FactAttribute
{
    public EnabledObsPackageFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENCAM_OBS_ENABLED_RUNTIME")))
            Skip = "Requires the explicitly enabled Windows user-test package; never enables a normal runtime.";
    }
}
