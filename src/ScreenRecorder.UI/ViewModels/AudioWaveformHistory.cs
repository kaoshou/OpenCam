// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.ViewModels;

public sealed record AudioWaveformBar(double Height);

/// <summary>Bounded, UI-thread-owned envelope history; never stores audio samples.</summary>
public sealed class AudioWaveformHistory
{
    private readonly double[] _values;

    public AudioWaveformHistory(int capacity = 24)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _values = new double[capacity];
    }

    public IReadOnlyList<double> Values => _values;

    public IReadOnlyList<AudioWaveformBar> Bars =>
        _values.Select(value => new AudioWaveformBar(value <= 0 ? 0 : 2 + value * 24)).ToArray();

    public void Push(AudioSourceLevel level)
    {
        if (level.State is AudioMeterState.Off or AudioMeterState.Paused or AudioMeterState.Unavailable)
        {
            Clear();
            return;
        }

        Array.Copy(_values, 1, _values, 0, _values.Length - 1);
        _values[^1] = double.IsFinite(level.Rms) ? Math.Clamp(level.Rms, 0, 1) : 0;
    }

    public void Clear() => Array.Clear(_values);
}
