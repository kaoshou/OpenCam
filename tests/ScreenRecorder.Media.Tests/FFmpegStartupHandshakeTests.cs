// SPDX-License-Identifier: AGPL-3.0-or-later
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

    [UnixOnlyFact]
    public async Task MicrophoneRecording_StartsNativeCaptureAndPassesItsPipeToProvider()
    {
        var executable = CreateExecutable(
            "echo 'frame=    1 fps=0.0 time=00:00:00.03' >&2\n" +
            "while IFS= read -r line; do [ \"$line\" = q ] && exit 0; done");
        var provider = new StubProvider();
        var microphoneCapture = new StubMicrophoneCapture();
        try
        {
            await using var engine = new FFmpegScreenRecorderEngine(
                provider,
                null,
                executable,
                microphoneCapture);

            await engine.StartRecordingAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"),
                new RecordingConfiguration
                {
                    EncoderType = HardwareEncoderType.SoftwareCpu,
                    AudioSource = AudioSourceType.MicrophoneOnly,
                    MicrophoneDeviceId = "0",
                    Fps = 30
                },
                new CaptureRegion(0, 0, 640, 480));

            Assert.True(microphoneCapture.IsCapturing);
            Assert.Equal(0.5, engine.ReadInputLevels(DateTimeOffset.UtcNow).Microphone?.Rms);
            Assert.Equal(
                StubMicrophoneCapture.PipeArguments,
                provider.LastMicrophoneAudioPipeArg);

            await engine.StopRecordingAsync();

            Assert.False(microphoneCapture.IsCapturing);
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [UnixOnlyFact]
    public async Task FailedStartup_ReleasesMicrophoneBeforeNextAttempt()
    {
        var executable = CreateExecutable("echo 'input failed' >&2\nexit 1");
        var microphoneCapture = new StubMicrophoneCapture();
        try
        {
            await using var engine = new FFmpegScreenRecorderEngine(
                new StubProvider(), null, executable, microphoneCapture);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                engine.StartRecordingAsync(
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"),
                    new RecordingConfiguration
                    {
                        EncoderType = HardwareEncoderType.SoftwareCpu,
                        AudioSource = AudioSourceType.MicrophoneOnly,
                        MicrophoneDeviceId = "0",
                        Fps = 30
                    },
                    new CaptureRegion(0, 0, 640, 480)));

            Assert.False(microphoneCapture.IsCapturing);
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [UnixOnlyFact]
    public async Task StopRecording_StopsMicrophoneProducerBeforeClosingFfmpegConsumer()
    {
        var markerPath = Path.Combine(
            Path.GetTempPath(),
            "opencam-microphone-stopped-" + Guid.NewGuid().ToString("N"));
        var observationPath = markerPath + ".observed";
        var executable = CreateExecutable(
            "echo 'frame=    1 fps=0.0 time=00:00:00.03' >&2\n" +
            "while IFS= read -r line; do\n" +
            "  if [ \"$line\" = q ]; then\n" +
            $"    if [ -f '{markerPath}' ]; then echo producer-stopped > '{observationPath}'; else echo consumer-closed-first > '{observationPath}'; fi\n" +
            "    exit 0\n" +
            "  fi\n" +
            "done");
        var microphoneCapture = new StubMicrophoneCapture(
            () => File.WriteAllText(markerPath, "stopped"));
        try
        {
            await using var engine = new FFmpegScreenRecorderEngine(
                new StubProvider(),
                null,
                executable,
                microphoneCapture);

            await engine.StartRecordingAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"),
                new RecordingConfiguration
                {
                    EncoderType = HardwareEncoderType.SoftwareCpu,
                    AudioSource = AudioSourceType.MicrophoneOnly,
                    MicrophoneDeviceId = "0",
                    Fps = 30
                },
                new CaptureRegion(0, 0, 640, 480));

            await engine.StopRecordingAsync();

            Assert.Equal(
                "producer-stopped\n",
                await File.ReadAllTextAsync(observationPath));
        }
        finally
        {
            File.Delete(executable);
            File.Delete(markerPath);
            File.Delete(observationPath);
        }
    }

    [UnixOnlyFact]
    public async Task Dispose_DoesNotDisposeInjectedMicrophoneCaptureOwnedByDependencyInjection()
    {
        var executable = CreateExecutable("exit 0");
        var microphoneCapture = new StubMicrophoneCapture();
        try
        {
            var engine = new FFmpegScreenRecorderEngine(
                new StubProvider(),
                null,
                executable,
                microphoneCapture);

            await engine.DisposeAsync();

            Assert.False(microphoneCapture.IsDisposed);
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
        public string? LastMicrophoneAudioPipeArg { get; private set; }

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
            string? systemAudioPipeArg = null,
            string? microphoneAudioPipeArg = null)
        {
            LastMicrophoneAudioPipeArg = microphoneAudioPipeArg;
            return string.Empty;
        }

        public string BuildOutputArguments(
            RecordingConfiguration config,
            HardwareEncoderType encoderType,
            string workingFilePath) => "-f null -";
    }

    private sealed class StubMicrophoneCapture : IMicrophoneCapture, IAudioLevelSource
    {
        public const string PipeArguments =
            "-thread_queue_size 1024 -f s16le -ar 48000 -ac 1 -i \"/tmp/microphone.pcm\"";

        public bool IsSupported => true;
        public bool IsCapturing { get; private set; }
        public bool IsDisposed { get; private set; }
        public AudioLevelSample? ReadLatestLevel(DateTimeOffset now) =>
            IsCapturing ? new AudioLevelSample(0.5, 0.8, now) : null;
        private readonly Action? _onStop;

        public StubMicrophoneCapture(Action? onStop = null)
        {
            _onStop = onStop;
        }

        public event EventHandler<string>? AudioErrorOccurred
        {
            add { }
            remove { }
        }

        public Task<MicrophoneCaptureInfo?> StartCaptureAsync(
            string? deviceId,
            CancellationToken cancellationToken = default)
        {
            IsCapturing = true;
            return Task.FromResult<MicrophoneCaptureInfo?>(new(
                "/tmp/microphone.pcm",
                48000,
                1,
                PipeArguments));
        }

        public Task StopCaptureAsync(
            CancellationToken cancellationToken = default)
        {
            _onStop?.Invoke();
            IsCapturing = false;
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            IsDisposed = true;
            await StopCaptureAsync();
        }
    }
}
