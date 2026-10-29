// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Media.Preflight;

/// <summary>Bounded statistics only. Never retains samples, paths or arbitrary FFmpeg output.</summary>
internal sealed class PreflightAudioMeters
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (double Rms, double Peak, DateTimeOffset At)> _values = new();

    public void Observe(string line, DateTimeOffset now)
    {
        var role = line.Contains("ametadata@oc_system_", StringComparison.Ordinal) ? "system" :
            line.Contains("ametadata@oc_microphone_", StringComparison.Ordinal) ? "microphone" :
            line.Contains("ametadata@oc_mix_", StringComparison.Ordinal) ? "mix" : null;
        if (role is null) return;
        const string peakKey = "lavfi.astats.Overall.Peak_level=";
        const string rmsKey = "lavfi.astats.Overall.RMS_level=";
        bool peak = line.Contains(peakKey, StringComparison.Ordinal);
        var key = peak ? peakKey : rmsKey;
        var start = line.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return;
        var text = line[(start + key.Length)..].Trim();
        double value;
        if (text == "-inf") value = 0;
        else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var db) && double.IsFinite(db))
            value = Math.Pow(10, Math.Min(db, 20) / 20);
        else return;
        lock (_gate)
        {
            _values.TryGetValue(role, out var old);
            _values[role] = peak ? (old.Rms, value, now) : (value, old.Peak, now);
        }
    }

    public AudioLevelSample? Read(string role, DateTimeOffset now)
    {
        lock (_gate)
            return _values.TryGetValue(role, out var value) && value.At <= now && now - value.At <= TimeSpan.FromSeconds(1)
                ? new(value.Rms, value.Peak, value.At) : null;
    }
}
