namespace ScreenRecorder.Platform.macOS;

public static class MacOsCursorSupport
{
    public const string HelperFileName = "OpenCam.CursorOverlay";

    public static string HelperPath(string baseDirectory) =>
        Path.Combine(baseDirectory, HelperFileName);
}
