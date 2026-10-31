// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Platform.Linux;
public static class LinuxRecordingLimits
{
    public static IReadOnlyList<int> FpsOptions{get;}=Array.AsReadOnly(new[]{5,10,20,30});
    public static int NormalizeFps(int fps)=>FpsOptions.Contains(fps)?fps:20;
}
