// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Audio;

/// <summary>Stores the latest bounded input level without retaining audio data.</summary>
public sealed class AudioLevelAccumulator
{
    private const int PcmSampleStride = 16;
    private const double DecibelFloor = -60;
    private sealed record SampleBox(AudioLevelSample Value);
    private SampleBox? _latest;

    public void PublishPcm16(ReadOnlySpan<byte> pcm, DateTimeOffset capturedAt, int channels = 1)
    {
        var sampleCount = pcm.Length / sizeof(short);
        if (sampleCount == 0 || channels <= 0 || sampleCount < channels)
        {
            return;
        }

        double squaredSum = 0;
        double peak = 0;
        var measured = 0;
        var frameCount = sampleCount / channels;
        var frameStride = frameCount <= PcmSampleStride ? 1 : Math.Max(1, PcmSampleStride / channels);
        for (var frame = 0; frame < frameCount; frame += frameStride)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = (frame * channels + channel) * sizeof(short);
                var value = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(offset, sizeof(short)));
                var magnitude = Math.Abs((double)value) / 32768;
                squaredSum += magnitude * magnitude;
                peak = Math.Max(peak, magnitude);
                measured++;
            }
        }

        PublishNormalized(Normalize(Math.Sqrt(squaredSum / measured)), Normalize(peak), capturedAt);
    }

    public void PublishNormalized(double rms, double peak, DateTimeOffset capturedAt)
    {
        if (!double.IsFinite(rms) || !double.IsFinite(peak))
        {
            return;
        }

        var sample = new AudioLevelSample(Math.Clamp(rms, 0, 1), Math.Clamp(peak, 0, 1), capturedAt);
        Interlocked.Exchange(ref _latest, new SampleBox(sample));
    }

    public AudioLevelSample? ReadFresh(DateTimeOffset now, TimeSpan maxAge)
    {
        var value = Volatile.Read(ref _latest)?.Value;
        if (value is null)
        {
            return null;
        }

        var age = now - value.Value.CapturedAt;
        return maxAge < TimeSpan.Zero || age < TimeSpan.Zero || age > maxAge || age > TimeSpan.FromSeconds(1)
            ? null
            : value;
    }

    public void Clear() => Interlocked.Exchange(ref _latest, null);

    private static double Normalize(double amplitude)
    {
        if (amplitude <= 0)
        {
            return 0;
        }

        var decibels = 20 * Math.Log10(amplitude);
        return Math.Clamp((decibels - DecibelFloor) / -DecibelFloor, 0, 1);
    }
}

public static class AudioLevelState
{
    private const double LiveThreshold = 0.04;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(1);

    public static AudioSourceLevel Resolve(bool enabled, RecordingState recordingState,
        AudioLevelSample? sample, DateTimeOffset now)
    {
        if (!enabled)
        {
            return new(AudioMeterState.Off, 0, 0);
        }

        if (recordingState is RecordingState.Paused or RecordingState.Pausing)
        {
            return new(AudioMeterState.Paused, 0, 0);
        }

        if (recordingState != RecordingState.Recording || sample is null ||
            sample.Value.CapturedAt > now || now - sample.Value.CapturedAt > MaximumAge ||
            !double.IsFinite(sample.Value.Rms) || !double.IsFinite(sample.Value.Peak))
        {
            return new(AudioMeterState.Unavailable, 0, 0);
        }

        var rms = Math.Clamp(sample.Value.Rms, 0, 1);
        var peak = Math.Clamp(sample.Value.Peak, 0, 1);
        return new(rms < LiveThreshold ? AudioMeterState.Silent : AudioMeterState.Live, rms, peak);
    }
}
