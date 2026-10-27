// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Models;

public sealed class AnnotationShortcut
{
    public uint Modifiers { get; set; }
    public string Key { get; set; } = "";

    public static Dictionary<string, AnnotationShortcut> Defaults() => new()
    {
        ["togglePen"] = new() { Key = "F10" },
        ["toggleToolbar"] = new() { Key = "F11" },
        ["finish"] = new() { Key = "Escape" },
        ["pen"] = new() { Modifiers = 6, Key = "F1" },
        ["line"] = new() { Modifiers = 6, Key = "F2" },
        ["rectangle"] = new() { Modifiers = 6, Key = "F3" },
        ["ellipse"] = new() { Modifiers = 6, Key = "F4" }
    };
}
