// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Media.Tests;

internal sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows APIs or Windows capture hardware.";
        }
    }
}

internal sealed class WindowsOnlyTheoryAttribute : TheoryAttribute
{
    public WindowsOnlyTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows APIs or Windows capture hardware.";
        }
    }
}

internal sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Requires a Unix executable test fixture.";
        }
    }
}

internal sealed class MacOsOnlyFactAttribute : FactAttribute
{
    public MacOsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "Requires macOS process behavior.";
        }
    }
}

// AvaloniaFactAttribute is sealed; retain its pinned 11.2.5 discoverer and add only the platform skip.
[Xunit.Sdk.XunitTestCaseDiscoverer("Avalonia.Headless.XUnit.AvaloniaUIFactDiscoverer", "Avalonia.Headless.XUnit")]
internal sealed class MacOsAvaloniaFactAttribute : FactAttribute
{
    public MacOsAvaloniaFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "Requires macOS capture preferences and an Avalonia service scope.";
        }
    }
}

internal sealed class RealMacHelperFactAttribute : FactAttribute
{
    public RealMacHelperFactAttribute()
    {
        if (!OperatingSystem.IsMacOS() ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENCAM_REAL_MIC_HELPER")))
        {
            Skip = "Set OPENCAM_REAL_MIC_HELPER on macOS to run the hardware helper test.";
        }
    }
}

internal sealed class RealRecorderFactAttribute : FactAttribute
{
    public RealRecorderFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENCAM_REAL_PIPE")))
        {
            Skip = "Set OPENCAM_REAL_PIPE to inspect a running recorder.";
        }
    }
}
