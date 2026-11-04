// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace ScreenRecorder.UI.Projects;

/// <summary>Bounded in-memory IPC round-trip aggregates, never media or project paths.</summary>
internal sealed class EditorRequestMetrics
{
    internal static EditorRequestMetrics Shared { get; } = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, (long Count, double Total, double Max)> _values = [];

    internal void Record(string operation, double milliseconds)
    {
        if (operation is not ("GetProjectFrame" or "PlayProjectPreview" or "GetProjectWaveform" or "GetProjectClips") ||
            !double.IsFinite(milliseconds) || milliseconds < 0) return;
        lock (_sync)
        {
            _values.TryGetValue(operation, out var value);
            _values[operation] = (value.Count + 1, value.Total + milliseconds, Math.Max(value.Max, milliseconds));
        }
    }
    internal string Describe()
    {
        lock (_sync)
            return "Editor IPC round-trip timings (this app session; not recording FPS):\n" +
                (_values.Count == 0 ? "No editor requests measured.\n" : string.Join("\n", _values.OrderBy(v => v.Key).Select(v =>
                    string.Create(CultureInfo.InvariantCulture, $"{v.Key}: count={v.Value.Count}, mean={v.Value.Total / v.Value.Count:F1} ms, max={v.Value.Max:F1} ms"))) + "\n");
    }
}
