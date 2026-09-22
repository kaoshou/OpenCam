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
