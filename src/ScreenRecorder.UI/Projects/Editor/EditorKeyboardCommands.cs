// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
namespace ScreenRecorder.UI.Projects.Editor;
public static class EditorKeyboardCommands
{
    public static bool OwnsInput(object? focused) => focused is Control c &&
        c.GetVisualAncestors().Prepend(c).Any(v => v is TextBox or NumericUpDown or Slider or ComboBox or Button or MenuItem or ListBox or Avalonia.Controls.Primitives.Popup);
    public static string? Resolve(Key key, KeyModifiers modifiers, bool ownsInput, bool mac)
    {
        var command = mac ? KeyModifiers.Meta : KeyModifiers.Control;
        if (modifiers == command && key == Key.S) return "save";
        if (ownsInput) return null;
        if (modifiers == command && key == Key.Z) return "undo";
        if (modifiers == (command|KeyModifiers.Shift) && key == Key.Z || !mac && modifiers==command && key==Key.Y) return "redo";
        if (modifiers==KeyModifiers.Shift) return key switch {Key.Left=>"secondPrevious",Key.Right=>"secondNext",_=>null};
        if (modifiers!=KeyModifiers.None) return null;
        return key switch {Key.Space=>"playPause",Key.Left=>"framePrevious",Key.Right=>"frameNext",Key.I=>"markIn",Key.O=>"markOut",_=>null};
    }
    public static string Hint(string command,bool mac) => command switch {
        "save" => mac?"⌘ S":"Ctrl+S", "undo"=>mac?"⌘ Z":"Ctrl+Z", "redo"=>mac?"⌘ ⇧ Z":"Ctrl+Shift+Z",
        "playPause"=>"Space", "framePrevious"=>"←", "frameNext"=>"→", "secondPrevious"=>"Shift+←", "secondNext"=>"Shift+→", "markIn"=>"I", "markOut"=>"O", _=>"" };
}
