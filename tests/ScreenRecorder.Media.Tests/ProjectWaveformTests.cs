// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectWaveformTests
{
    [Fact]
    public void KnownPulse_PeakOccursOnlyInExpectedBucket()
    {
        var builder = new ProjectWaveformBuilder(8, 4, true);
        builder.Append(Pcm(0, 0, 0, 0, -.75f, .5f, 0, 0));
        var result = builder.Complete();
        Assert.Equal(4, result.Buckets.Length);
        Assert.Equal(-.75f, result.Buckets[2].Minimum);
        Assert.Equal(.5f, result.Buckets[2].Maximum);
        Assert.InRange(result.Buckets[2].Rms, .637f, .638f);
        Assert.Equal(0, result.Buckets[0].Rms);
        Assert.Equal(0, result.Buckets[1].Rms);
        Assert.Equal(0, result.Buckets[3].Rms);
        Assert.False(result.IsSilent);
    }

    [Fact]
    public void ArbitraryByteChunks_AreIdenticalToOneBuffer()
    {
        var bytes = Pcm(.1f, -.2f, .3f, -.4f, .5f, -.6f, .7f);
        var whole = new ProjectWaveformBuilder(7, 3, true);
        whole.Append(bytes);
        var chunks = new ProjectWaveformBuilder(7, 3, true);
        foreach (var value in bytes) chunks.Append([value]);
        Assert.Equal(whole.Complete().Buckets.ToArray(), chunks.Complete().Buckets.ToArray());
    }

    [Fact]
    public void AbsentAudio_IsDistinctFromPresentButSilentAudio()
    {
        var absent = new ProjectWaveformBuilder(4, 2, false);
        var silent = new ProjectWaveformBuilder(4, 2, true);
        absent.Append(Pcm(0, 0, 0, 0));
        silent.Append(Pcm(0, 0, 0, 0));
        Assert.False(absent.Complete().HasAudio);
        Assert.True(silent.Complete().HasAudio);
        Assert.True(silent.Complete().IsSilent);
    }

    [Fact]
    public void TruncatedData_CannotProduceSuccessfulWaveform()
    {
        var builder = new ProjectWaveformBuilder(4, 2, true);
        builder.Append(Pcm(0, 0, 0));
        Assert.Throws<InvalidDataException>(() => builder.Complete());
    }

    [Fact]
    public void InvalidPcm_IsRejected()
    {
        var builder = new ProjectWaveformBuilder(1, 1, true);
        Assert.Throws<InvalidDataException>(() => builder.Append(Pcm(float.NaN)));
    }

    [Fact]
    public void ExtraSamples_AreRejected()
    {
        var builder = new ProjectWaveformBuilder(1, 1, true);
        Assert.Throws<InvalidDataException>(() => builder.Append(Pcm(0, 1)));
    }

    private static byte[] Pcm(params float[] samples)
    {
        var result = new byte[samples.Length * 4];
        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(i * 4), BitConverter.SingleToInt32Bits(samples[i]));
        return result;
    }
}
