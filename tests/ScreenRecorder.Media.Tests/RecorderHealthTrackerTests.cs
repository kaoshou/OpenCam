// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public class RecorderHealthTrackerTests
{
    [Fact]
    public void BeforeFirstProgress_HealthIsUnknown()
    {
        var tracker = CreateTracker(AudioSourceType.None);

        var health = tracker.CreateTelemetryHealth(OffAudio());

        Assert.Null(health.DroppedFrames);
        Assert.Null(health.VideoHealthy);
        Assert.Null(health.EncoderHealthy);
    }

    [Fact]
    public void RunningProgress_HealthBecomesTrue()
    {
        var tracker = CreateTracker(AudioSourceType.None);

        tracker.ObserveProgress(new(true, 1, TimeSpan.FromMilliseconds(50), 1_024));

        var health = tracker.CreateTelemetryHealth(OffAudio());
        Assert.True(health.VideoHealthy);
        Assert.True(health.EncoderHealthy);
        Assert.Null(health.DroppedFrames);
    }

    [Fact]
    public void ThreeUnchangedObservations_MarksVideoAndEncoderUnhealthy()
    {
        var tracker = CreateTracker(AudioSourceType.None);
        var observation = new RecorderProgressObservation(
            true, 25, TimeSpan.FromSeconds(1), 4_096);
        tracker.ObserveProgress(observation);

        tracker.ObserveProgress(observation);
        tracker.ObserveProgress(observation);
        tracker.ObserveProgress(observation);

        var health = tracker.CreateTelemetryHealth(OffAudio());
        Assert.False(health.VideoHealthy);
        Assert.False(health.EncoderHealthy);
        Assert.NotNull(health.Warning);
    }

    [Fact]
    public void BeginSegment_ClearsPreviousStallHistory()
    {
        var tracker = CreateTracker(AudioSourceType.None);
        var observation = new RecorderProgressObservation(
            true, 25, TimeSpan.FromSeconds(1), 4_096);
        tracker.ObserveProgress(observation);
        tracker.ObserveProgress(observation);
        tracker.ObserveProgress(observation);
        tracker.BeginSegment();

        tracker.ObserveProgress(new(true, 1, TimeSpan.FromMilliseconds(20), 512));

        var health = tracker.CreateTelemetryHealth(OffAudio());
        Assert.True(health.VideoHealthy);
        Assert.True(health.EncoderHealthy);
        Assert.Null(health.Warning);
    }

    [Fact]
    public void EngineExitOrError_MarksEncoderUnhealthyAndPreservesMessage()
    {
        var tracker = CreateTracker(AudioSourceType.None);
        tracker.RecordEngineError("FFmpeg exited unexpectedly");
        tracker.BeginSegment();
        tracker.ObserveProgress(new(false, 0, TimeSpan.Zero, 0));

        var health = tracker.CreateTelemetryHealth(OffAudio());
        Assert.False(health.EncoderHealthy);
        Assert.Contains("FFmpeg exited unexpectedly", health.Warning);
    }

    [Fact]
    public void FreshSilentAudioSample_RemainsHealthy()
    {
        var tracker = CreateTracker(AudioSourceType.SystemAndMicrophone);
        var silent = new AudioSourceLevel(AudioMeterState.Silent, 0, 0);

        var health = tracker.CreateTelemetryHealth(new(silent, silent));

        Assert.True(health.SystemAudioHealthy);
        Assert.True(health.MicrophoneHealthy);
    }

    [Fact]
    public void SelectedAudioWithoutFreshSample_IsUnavailable()
    {
        var tracker = CreateTracker(AudioSourceType.SystemAndMicrophone);
        var unavailable = new AudioSourceLevel(AudioMeterState.Unavailable, 0, 0);

        var health = tracker.CreateTelemetryHealth(new(unavailable, unavailable));

        Assert.False(health.SystemAudioHealthy);
        Assert.False(health.MicrophoneHealthy);
    }

    [Theory]
    [InlineData(AudioSourceType.None, null, null)]
    [InlineData(AudioSourceType.SystemOnly, true, null)]
    [InlineData(AudioSourceType.MicrophoneOnly, null, true)]
    public void UnselectedAudioSource_HealthIsNull(
        AudioSourceType source,
        bool? expectedSystem,
        bool? expectedMicrophone)
    {
        var tracker = CreateTracker(source);
        var silent = new AudioSourceLevel(AudioMeterState.Silent, 0, 0);

        var health = tracker.CreateTelemetryHealth(new(silent, silent));

        Assert.Equal(expectedSystem, health.SystemAudioHealthy);
        Assert.Equal(expectedMicrophone, health.MicrophoneHealthy);
    }

    private static RecorderHealthTracker CreateTracker(AudioSourceType source)
    {
        var tracker = new RecorderHealthTracker();
        tracker.Reset(new RecordingConfiguration { AudioSource = source });
        return tracker;
    }

    private static AudioLevelsSnapshot OffAudio() => new(
        new AudioSourceLevel(AudioMeterState.Off, 0, 0),
        new AudioSourceLevel(AudioMeterState.Off, 0, 0));
}
