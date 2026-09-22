// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Audio;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Tests;

public sealed class AudioLevelAccumulatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pcm16_ReportsMeasuredRmsAndPeakWithoutChangingInput()
    {
        var meter = new AudioLevelAccumulator();
        byte[] pcm = [0, 0, 0xff, 0x7f];

        meter.PublishPcm16(pcm, Now);

        Assert.Equal(new byte[] { 0, 0, 0xff, 0x7f }, pcm);
        var sample = Assert.IsType<AudioLevelSample>(meter.ReadFresh(Now, TimeSpan.FromSeconds(1)));
        Assert.InRange(sample.Rms, 0.90, 0.99);
        Assert.InRange(sample.Peak, 0.99, 1.0);
        Assert.Equal(Now, sample.CapturedAt);
    }

    [Fact]
    public void EmptyOrOddPcm_DoesNotThrowOrInventLevel()
    {
        var meter = new AudioLevelAccumulator();

        meter.PublishPcm16(ReadOnlySpan<byte>.Empty, Now);
        meter.PublishPcm16(new byte[] { 0xff }, Now);

        Assert.Null(meter.ReadFresh(Now, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void SilentAndClippedPcm_AreFiniteWithinRange()
    {
        var meter = new AudioLevelAccumulator();
        meter.PublishPcm16(new byte[] { 0, 0, 0, 0 }, Now);
        var silent = meter.ReadFresh(Now, TimeSpan.FromSeconds(1));
        Assert.NotNull(silent);
        Assert.Equal(0, silent.Value.Rms);
        Assert.Equal(0, silent.Value.Peak);

        meter.PublishPcm16(new byte[] { 0, 0x80, 0 }, Now);
        var clipped = meter.ReadFresh(Now, TimeSpan.FromSeconds(1));
        Assert.NotNull(clipped);
        Assert.Equal(1, clipped.Value.Rms);
        Assert.Equal(1, clipped.Value.Peak);
    }

    [Fact]
    public void NormalizedInput_ClampsFiniteValuesAndIgnoresMalformedValues()
    {
        var meter = new AudioLevelAccumulator();
        meter.PublishNormalized(double.NaN, 0.5, Now);
        meter.PublishNormalized(0.5, double.PositiveInfinity, Now);
        Assert.Null(meter.ReadFresh(Now, TimeSpan.FromSeconds(1)));

        meter.PublishNormalized(-0.5, 2.0, Now);
        var sample = meter.ReadFresh(Now, TimeSpan.FromSeconds(1));
        Assert.NotNull(sample);
        Assert.Equal(0, sample.Value.Rms);
        Assert.Equal(1, sample.Value.Peak);
    }

    [Fact]
    public void ReadFresh_DropsFutureAndStaleSamples()
    {
        var meter = new AudioLevelAccumulator();
        meter.PublishNormalized(0.5, 0.8, Now);

        Assert.Null(meter.ReadFresh(Now.AddTicks(-1), TimeSpan.FromSeconds(1)));
        Assert.NotNull(meter.ReadFresh(Now.AddSeconds(1), TimeSpan.FromSeconds(1)));
        Assert.Null(meter.ReadFresh(Now.AddSeconds(1).AddTicks(1), TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(false, RecordingState.Recording, AudioMeterState.Off)]
    [InlineData(true, RecordingState.Preparing, AudioMeterState.Unavailable)]
    [InlineData(true, RecordingState.Pausing, AudioMeterState.Paused)]
    [InlineData(true, RecordingState.Paused, AudioMeterState.Paused)]
    [InlineData(true, RecordingState.Recording, AudioMeterState.Unavailable)]
    [InlineData(true, RecordingState.Stopping, AudioMeterState.Unavailable)]
    public void StateResolver_UsesRequestedSourceAndRecordingLifecycle(
        bool enabled, RecordingState recordingState, AudioMeterState expected)
    {
        var result = AudioLevelState.Resolve(enabled, recordingState, null, Now);

        Assert.Equal(expected, result.State);
        Assert.Equal(0, result.Rms);
        Assert.Equal(0, result.Peak);
    }

    [Fact]
    public void StateResolver_DistinguishesLiveSilentAndStale()
    {
        var silent = new AudioLevelSample(0.039, 0.2, Now);
        var live = new AudioLevelSample(0.04, 0.5, Now);

        Assert.Equal(AudioMeterState.Silent,
            AudioLevelState.Resolve(true, RecordingState.Recording, silent, Now).State);
        Assert.Equal(AudioMeterState.Live,
            AudioLevelState.Resolve(true, RecordingState.Recording, live, Now).State);
        Assert.Equal(AudioMeterState.Unavailable,
            AudioLevelState.Resolve(true, RecordingState.Recording, live, Now.AddSeconds(2)).State);
        Assert.Equal(AudioMeterState.Paused,
            AudioLevelState.Resolve(true, RecordingState.Paused, live, Now).State);
    }
}
