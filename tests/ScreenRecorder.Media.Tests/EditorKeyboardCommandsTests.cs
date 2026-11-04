// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Input;
using Avalonia.Controls;
using ScreenRecorder.UI.Projects.Editor;
namespace ScreenRecorder.Media.Tests;
public sealed class EditorKeyboardCommandsTests
{
    [Theory]
    [InlineData(Key.Space,KeyModifiers.None,"playPause")]
    [InlineData(Key.Left,KeyModifiers.None,"framePrevious")]
    [InlineData(Key.Right,KeyModifiers.Shift,"secondNext")]
    [InlineData(Key.I,KeyModifiers.None,"markIn")]
    [InlineData(Key.O,KeyModifiers.None,"markOut")]
    public void KeysAreScopedAndExact(Key key,KeyModifiers modifiers,string action)
    {
        Assert.Equal(action,EditorKeyboardCommands.Resolve(key,modifiers,false,false));
        Assert.Null(EditorKeyboardCommands.Resolve(key,modifiers,true,false));
        Assert.Null(EditorKeyboardCommands.Resolve(key,modifiers|KeyModifiers.Alt,false,false));
    }
    [Fact]
    public void NativeTextUndoAndExplicitSaveRemainDistinct()
    {
        Assert.Null(EditorKeyboardCommands.Resolve(Key.Z,KeyModifiers.Control,true,false));
        Assert.Equal("save",EditorKeyboardCommands.Resolve(Key.S,KeyModifiers.Meta,true,true));
        Assert.Null(EditorKeyboardCommands.Resolve(Key.S,KeyModifiers.Control,false,true));
        Assert.Equal("redo",EditorKeyboardCommands.Resolve(Key.Z,KeyModifiers.Meta|KeyModifiers.Shift,false,true));
        Assert.True(EditorKeyboardCommands.OwnsInput(new TextBox()));
        Assert.True(EditorKeyboardCommands.OwnsInput(new NumericUpDown()));
        Assert.True(EditorKeyboardCommands.OwnsInput(new Slider()));
        Assert.True(EditorKeyboardCommands.OwnsInput(new Button()));
        Assert.True(EditorKeyboardCommands.OwnsInput(new ComboBox()));
    }
}
