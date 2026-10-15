// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.UI.Recording;

public sealed class RecordingHotkeyBindings(IGlobalHotkeyService native)
{
    private readonly Dictionary<int, (int Key, uint Mods)> _active = [];
    public bool Apply((int Id, int Key, uint Mods)[] requested)
    {
        var changed = new List<int>();
        foreach (var binding in requested)
        {
            if (_active.TryGetValue(binding.Id, out var old) && old == (binding.Key, binding.Mods)) continue;
            if (native.RegisterHotkey(binding.Id, binding.Key, binding.Mods)) { changed.Add(binding.Id); continue; }
            foreach (var id in changed.AsEnumerable().Reverse())
                if (_active.TryGetValue(id, out var previous))
                {
                    if (!native.RegisterHotkey(id, previous.Key, previous.Mods))
                        throw new InvalidOperationException("Shortcut registration failed and rollback failed; reopen settings to re-register shortcuts.");
                }
                else native.UnregisterHotkey(id);
            return false;
        }
        foreach (var binding in requested) _active[binding.Id] = (binding.Key, binding.Mods);
        return true;
    }
}
