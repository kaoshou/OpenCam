using ScreenRecorder.Platform.Windows.Display;
using ScreenRecorder.Platform.Windows.Audio;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.State;
using ScreenRecorder.Infrastructure.Diagnostics;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Infrastructure.Storage;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Remux;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Recorder.Services;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class AudioRecordingModesTests : IDisposable
{
    private readonly string _testRoot;

    public AudioRecordingModesTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "AudioModesTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [WindowsOnlyTheory]
    [InlineData(AudioSourceType.None, 0)]
    [InlineData(AudioSourceType.SystemOnly, 1)]
    [InlineData(AudioSourceType.MicrophoneOnly, 1)]
    [InlineData(AudioSourceType.SystemAndMicrophone, 1)]
    public async Task AudioModes_Recording_ShouldProduceCorrectAudioStreamCount(AudioSourceType audioSource, int expectedAudioStreams)
    {
        var stateMachine = new RecordingStateMachine();
        var storageService = new StorageService();
        var sessionStore = new JsonRecordingSessionStore();
        var diskMonitor = new DiskSpaceMonitor(storageService);
        var remuxer = new StreamCopyRemuxer();
        var probe = new MediaFileProbe();
        var displayService = new WindowsDisplayService();

        await using var orchestrator = new RecordingOrchestrator(
            stateMachine,
            storageService,
            sessionStore,
            diskMonitor,
            remuxer,
            probe,
            displayService, new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider())
        {
            UseSyntheticCaptureSource = true
        };

        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            Region = new CaptureRegion(0, 0, 320, 240),
            Fps = 30,
            AudioSource = audioSource,
            OutputDirectory = _testRoot
        };

        var (startSuccess, startError, sessionId) = await orchestrator.StartRecordingAsync(config);
        Assert.True(startSuccess, $"啟動錄影失敗: {startError}");

        // 錄製 1.5 秒
        await Task.Delay(1500);

        var (stopSuccess, stopError, finalMp4) = await orchestrator.StopRecordingAsync();
        Assert.True(stopSuccess, $"停止錄影失敗: {stopError}");
        Assert.NotNull(finalMp4);
        Assert.True(File.Exists(finalMp4));

        var probeResult = await probe.ProbeAsync(finalMp4!);
        Assert.True(probeResult.IsValid, "MP4 探針檢驗無效");
        Assert.Equal(1, probeResult.VideoStreamCount);
        Assert.Equal(expectedAudioStreams, probeResult.AudioStreamCount);

        if (expectedAudioStreams > 0)
        {
            Assert.Equal("aac", probeResult.AudioCodec);
            Assert.Equal(44100, probeResult.SampleRate);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch { }
    }
}
