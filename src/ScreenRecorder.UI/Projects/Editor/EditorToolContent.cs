// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace ScreenRecorder.UI.Projects.Editor;

/// <summary>Resolution-independent, consistently sized line icons with visible action labels.</summary>
public sealed class EditorToolContent : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<EditorToolContent, string>(nameof(Text), "");
    public static readonly StyledProperty<string> IconProperty = AvaloniaProperty.Register<EditorToolContent, string>(nameof(Icon), "clips");
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly Avalonia.Controls.Shapes.Path _icon = new() { Width = 16, Height = 16, Stretch = Stretch.Uniform,
        StrokeThickness = 1.5, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        VerticalAlignment = VerticalAlignment.Center };
    public EditorToolContent()
    {
        _icon.Bind(Shape.StrokeProperty, this.GetObservable(ForegroundProperty));
        _label.Bind(TextBlock.ForegroundProperty, this.GetObservable(ForegroundProperty));
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _icon, _label } };
        UpdateLabel(); UpdateIcon();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) UpdateLabel();
        if (change.Property == IconProperty) UpdateIcon();
    }
    private void UpdateLabel()
    {
        _label.Text = Text;
        _label.IsVisible = Text.Length > 0;
    }
    private void UpdateIcon()
    {
        _icon.Data = Geometry.Parse(Icon switch {
            "fullscreen" => "M6,1 H1 V6 M10,1 H15 V6 M1,10 V15 H6 M10,15 H15 V10",
            "sound-on" => "M1,6 H4 L8,2 V14 L4,10 H1 Z M11,5 Q15,8 11,11",
            "sound-off" => "M1,6 H4 L8,2 V14 L4,10 H1 Z M11,6 L15,10 M15,6 L11,10",
            "new" => "M8,2 V14 M2,8 H14",
            "folder" => "M1,3 H6 L8,5 H15 V13 H1 Z",
            "settings" => "M2,4 H14 M2,8 H14 M2,12 H14 M5,2 V6 M11,6 V10 M6,10 V14",
            "back" => "M7,2 L1,8 L7,14 M1,8 H15",
            "rename" => "M3,10 L10.5,2.5 Q11.5,1.5 12.5,2.5 L13.5,3.5 Q14.5,4.5 13.5,5.5 L6,13 L2.5,13.5 Z M9.5,3.5 L12.5,6.5",
            "edit-recording" => "M7,13 H2 V2 H14 V6 M2,5 H14 M5,2 V5 M11,2 V5 M9,11 L13,7 L15,9 L11,13 L8,14 Z",
            "properties" => "M2,4 H14 M2,8 H14 M2,12 H14 M5,2 V6 M11,6 V10 M6,10 V14",
            "split" => "M6,6 L14,14 M6,10 L14,2 M3,3 A2.5,2.5 0 1 0 3,8 A2.5,2.5 0 1 0 3,3 M3,9 A2.5,2.5 0 1 0 3,14 A2.5,2.5 0 1 0 3,9",
            "undo" => "M6,3 L2,7 L6,11 M2,7 H10 C15,7 15,14 10,14",
            "redo" => "M10,3 L14,7 L10,11 M14,7 H6 C1,7 1,14 6,14",
            "start" => "M6,2 H3 V14 H6 M10,5 L13,8 L10,11",
            "end" => "M10,2 H13 V14 H10 M6,5 L3,8 L6,11",
            "delete" => "M2,4 H14 M6,4 V2 H10 V4 M4,4 L5,14 H11 L12,4 M7,7 V11 M9,7 V11",
            "fit" => "M5,2 H2 V5 M11,2 H14 V5 M2,11 V14 H5 M14,11 V14 H11 M5,8 H11",
            "thumbnail" => "M2,2 H14 V14 H2 Z M3,11 L6,7 L9,10 L11,8 L14,12 M10,5 H11",
            "export" => "M8,11 V2 M4,6 L8,2 L12,6 M2,10 V14 H14 V10",
            "insert" => "M2,2 V14 M14,2 V14 M8,4 V12 M4,8 H12",
            _ => "M2,3 H3 M6,3 H14 M2,8 H3 M6,8 H14 M2,13 H3 M6,13 H14"
        });
    }
}
