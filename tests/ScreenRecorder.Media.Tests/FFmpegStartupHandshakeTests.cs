using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Media.Tests;

public class FFmpegStartupHandshakeTests
{
    [UnixOnlyFact]
    public async Task DelayedExitBeforeFirstFrame_IsRejected()
    {
        var executable = CreateExecutable(
            "sleep 0.6\necho 'Error while opening encoder' >&2\nexit 1");
        try
        {
            await using var engine = new FFmpegScreenRecorderEngine(
                new StubProvider(), null, executable);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                engine.StartRecordingAsync(
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"),
                    Config(),
                    new CaptureRegion(0, 0, 640, 480)));

            Assert.Contains("初始化失敗", error.Message);
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [UnixOnlyFact]
    public async Task FirstFrame_CompletesStartupHandshake()
    {
        var executable = CreateExecutable(
            "echo 'frame=    1 fps=0.0 time=00:00:00.03' >&2\n" +
            "while IFS= read -r line; do [ \"$line\" = q ] && exit 0; done");
        try
        {
            await using var engine = new FFmpegScreenRecorderEngine(
                new StubProvider(), null, executable);

            await engine.StartRecordingAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"),
                Config(),
                new CaptureRegion(0, 0, 640, 480));

            Assert.True(engine.IsRunning);
            await engine.StopRecordingAsync();
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [UnixOnlyFact]
    public async Task Launch_UsesNonInteractiveOverwriteForEncoderFallback()
    {
        var executable = CreateExecutable(
            "[ \"$1\" = '-y' ] || { echo 'missing -y' >&2; exit 1; }\n" +
            "echo 'frame=    1 fps=0.0 time=00:00:00.03' >&2\n" +
            "while IFS= read -r line; do [ \"$line\" = q ] && exit 0; done");
        try
        {
            await using var engine = new FFmpegScreenRecorderEngine(
                new StubProvider(), null, executable);

            await engine.StartRecordingAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"),
                Config(),
                new CaptureRegion(0, 0, 640, 480));

            Assert.True(engine.IsRunning);
            await engine.StopRecordingAsync();
        }
        finally
        {
            File.Delete(executable);
        }
    }

    private static RecordingConfiguration Config() => new()
    {
        EncoderType = HardwareEncoderType.SoftwareCpu,
        AudioSource = AudioSourceType.None,
        Fps = 30
    };

    private static string CreateExecutable(string body)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Unix executable fixture required.");
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            "opencam-ffmpeg-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
        return path;
    }

    private sealed class StubProvider : IFFmpegPlatformProvider
    {
        public IEnumerable<(string EncoderName, string ExtraArgs, HardwareEncoderType Type)>
            GetHardwareEncoderProbes() => [];

        public string BuildInputArguments(
            RecordingConfiguration config,
            int x,
            int y,
            int width,
            int height,
            bool useSynthetic,
            bool hasDirectShowMic,
            string? systemAudioPipeArg = null) => string.Empty;

        public string BuildOutputArguments(
            RecordingConfiguration config,
            HardwareEncoderType encoderType,
            string workingFilePath) => "-f null -";
    }
}
