// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

/// <summary>Constant-memory reduction of mono f32le PCM. Buffers may end in the middle of a sample.</summary>
public sealed class ProjectWaveformBuilder
{
    private readonly long _expectedSamples;
    private readonly bool _hasAudio;
    private readonly float[] _minimum, _maximum;
    private readonly double[] _squares;
    private readonly long[] _counts;
    private readonly byte[] _partial = new byte[4];
    private int _partialCount, _bucket;
    private long _samples, _nextBoundary;
    private bool _failed;

    public ProjectWaveformBuilder(long sampleCount, int bucketCount, bool hasAudio)
    {
        if (sampleCount <= 0 || bucketCount is <= 0 or > 4096 || bucketCount > sampleCount)
            throw new ArgumentOutOfRangeException(nameof(bucketCount));
        _expectedSamples = sampleCount;
        _hasAudio = hasAudio;
        _minimum = Enumerable.Repeat(float.PositiveInfinity, bucketCount).ToArray();
        _maximum = Enumerable.Repeat(float.NegativeInfinity, bucketCount).ToArray();
        _squares = new double[bucketCount];
        _counts = new long[bucketCount];
        _nextBoundary = Boundary(1);
    }

    public void Append(ReadOnlySpan<byte> pcm)
    {
        if (_failed) throw new InvalidDataException("Waveform input was previously rejected.");
        try
        {
            foreach (var value in pcm)
            {
                _partial[_partialCount++] = value;
                if (_partialCount != 4) continue;
                _partialCount = 0;
                var sample = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_partial));
                if (!float.IsFinite(sample) || _samples >= _expectedSamples)
                    throw new InvalidDataException("Invalid or excess PCM samples.");
                if (_samples >= _nextBoundary) { _bucket++; _nextBoundary = Boundary(_bucket + 1); }
                _minimum[_bucket] = Math.Min(_minimum[_bucket], sample);
                _maximum[_bucket] = Math.Max(_maximum[_bucket], sample);
                _squares[_bucket] += (double)sample * sample;
                _counts[_bucket]++;
                _samples++;
            }
        }
        catch { _failed = true; throw; }
    }

    public ProjectWaveformResult Complete()
    {
        if (_failed || _partialCount != 0 || _samples != _expectedSamples)
            throw new InvalidDataException("Incomplete or invalid waveform input.");
        var result = ImmutableArray.CreateBuilder<ProjectWaveformBucket>(_counts.Length);
        var silent = true;
        for (var i = 0; i < _counts.Length; i++)
        {
            result.Add(new(_minimum[i], _maximum[i], (float)Math.Sqrt(_squares[i] / _counts[i])));
            silent &= _minimum[i] == 0 && _maximum[i] == 0;
        }
        return new(_hasAudio, silent, _samples, result.MoveToImmutable());
    }

    private long Boundary(int bucket) =>
        (long)(((BigInteger)bucket * _expectedSamples + _counts.Length - 1) / _counts.Length);
}
