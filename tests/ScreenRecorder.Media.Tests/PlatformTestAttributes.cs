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
