namespace ScreenRecorder.Platform.macOS;

public static class MacOsMicrophoneSupport
{
    public const string HelperFileName = "OpenCam.Microphone";

    public static string HelperPath(string baseDirectory) =>
        Path.Combine(baseDirectory, HelperFileName);

    public static bool IsSupported(string baseDirectory) =>
        File.Exists(HelperPath(baseDirectory));
}
