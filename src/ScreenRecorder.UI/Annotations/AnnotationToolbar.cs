// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ScreenRecorder.Core.Annotations;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Annotations;

/// <summary>One small, disposable tool window; no recorder/view-model dependency.</summary>
internal sealed class AnnotationToolbar:Window,IDisposable
{
    private readonly AnnotationDocument _document;
    private readonly Action<bool> _mode;
    private readonly Action _close;
    private readonly Action<uint,double> _style;
    private readonly Action<Exception> _failure;
    private readonly Action? _minimize;
    private readonly Action? _hide;
    private readonly Func<bool>? _canHide;
    private readonly StackPanel _root=new(){Spacing=8};
    private bool _drawing,_compact;
    private bool _disposed;
    private Button? _undo,_redo;
    private Button? _operate,_hideButton;
    private Button? _collapseButton;
    private Control? _tools,_appearance,_help;
    private TextBlock? _title;
    private readonly Dictionary<AnnotationTool,Button> _toolButtons=[];
    private readonly Dictionary<uint,Button> _colorButtons=[];
    private readonly Dictionary<double,Button> _widthButtons=[];
    private LanguageManager Strings=>LanguageManager.Instance;
    public AnnotationToolbar(AnnotationDocument document,Action<bool> mode,Action close,Action<uint,double> style,Action<Exception> failure,Action? minimize=null,Action? hide=null,Func<bool>? canHide=null,Func<Key,KeyModifiers,bool>? finishShortcut=null)
    {
        _document=document;_mode=mode;_close=close;_style=style;_failure=failure;_minimize=minimize;_hide=hide;_canHide=canHide;
        Width=480;MinWidth=480;MaxWidth=480;SizeToContent=SizeToContent.Height;CanResize=false;SystemDecorations=SystemDecorations.None;
        ShowInTaskbar=false;Topmost=true;ShowActivated=false;Background=Brushes.Transparent;
        TransparencyLevelHint=[WindowTransparencyLevel.Transparent];
        Content=new Border{Background=Brushes.White,BorderBrush=Brush.Parse("#CBD5E1"),BorderThickness=new(1),CornerRadius=new(12),Padding=new(12),Child=_root};
        document.Changed+=Changed;Strings.PropertyChanged+=Changed;
        Closed+=(_,_)=>Dispose();
        KeyDown+=(_,e)=>{if((finishShortcut??((k,m)=>k==Key.Escape&&m==KeyModifiers.None))(e.Key,e.KeyModifiers)){Run(()=>{_mode(false);_document.Clear();});e.Handled=true;}};
        Rebuild();
    }
    public void SetMode(bool drawing){_drawing=drawing;UpdateToolSelection();}
    private void Changed(object? sender,EventArgs e)
    {
        if(ReferenceEquals(sender,_document))
        {if(_undo is not null)_undo.IsEnabled=_document.CanUndo;if(_redo is not null)_redo.IsEnabled=_document.CanRedo;}
        else Rebuild();
    }
    private Button Button(string key,string? path,Action action,bool selected=false)
    {
        var label=Strings[key];var b=new Button{MinWidth=34,Height=34,Padding=new(8,5),CornerRadius=new(6),
            Background=Brush.Parse(selected?"#DBEAFE":"#F8FAFC"),BorderBrush=Brush.Parse(selected?"#3B82F6":"#E2E8F0"),BorderThickness=new(1),
            Foreground=Brush.Parse("#1E293B"),Content=path is null?new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,FontSize=12}:
                CreateIcon(path)};
        ToolTip.SetTip(b,label);ToolTip.SetShowOnDisabled(b,true);AutomationProperties.SetName(b,label);b.Click+=(_,_)=>Run(action);return b;
    }
    private static Canvas CreateIcon(string path)
    {
        // A fixed coordinate space also renders horizontal or vertical strokes;
        // Stretch.Uniform cannot scale a zero-height/zero-width geometry reliably.
        var canvas=new Canvas{Width=18,Height=18};
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path{Data=Geometry.Parse(path),
            Stroke=Brush.Parse("#334155"),StrokeThickness=1.7,StrokeLineCap=PenLineCap.Round,StrokeJoin=PenLineJoin.Round});
        return canvas;
    }
    private Button HeaderButton(string key,string path,Action action)
    {
        var button=Button(key,path,action);
        button.Width=34;button.Padding=new(0);button.Margin=new(4,0,0,0);
        button.HorizontalContentAlignment=HorizontalAlignment.Center;
        button.VerticalContentAlignment=VerticalAlignment.Center;
        if(button.Content is Canvas canvas && canvas.Children[0] is Avalonia.Controls.Shapes.Path icon)
        {icon.Width=18;icon.Height=18;icon.Stretch=Stretch.None;icon.StrokeThickness=1.8;}
        return button;
    }
    private void Run(Action action){try{action();}catch(Exception e){_failure(e);}}
    private void Rebuild()
    {
        Title=Strings["AnnotationsTitle"];
        _root.Children.Clear();
        _colorButtons.Clear();_widthButtons.Clear();
        _toolButtons.Clear();_operate=null;_hideButton=null;
        var header=new Grid{ColumnDefinitions=new("*,Auto,Auto,Auto,Auto")};
        var title=new TextBlock{Text="⠿  "+Strings["AnnotationsTitle"]+" · "+Strings[_drawing?"AnnotationsDrawing":"AnnotationsOperating"],
            FontSize=12,FontWeight=FontWeight.SemiBold,Foreground=Brush.Parse("#334155"),VerticalAlignment=VerticalAlignment.Center};
        _title=title;
        var dragArea=new Border{Background=Brushes.Transparent,Child=title};
        ToolTip.SetTip(dragArea,Strings["AnnotationsDrag"]);
        dragArea.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)Run(()=>BeginMoveDrag(e));};
        header.Children.Add(dragArea);
        if(_minimize is not null){var minimize=HeaderButton("AnnotationsMinimize","M3,11 L15,11",_minimize);Grid.SetColumn(minimize,1);header.Children.Add(minimize);}
        var canHide=_hide is not null&&(_canHide?.Invoke()??true);
        if(_hide is not null){_hideButton=HeaderButton("AnnotationsHide","M3,4 L15,4 L15,8 L3,8 Z M9,10 L9,15 M6,12 L9,15 L12,12",_hide);_hideButton.IsVisible=canHide;Grid.SetColumn(_hideButton,2);header.Children.Add(_hideButton);}
        _collapseButton=HeaderButton("AnnotationsCollapse","M4,11 L9,6 L14,11",()=>{_compact=!_compact;UpdateCompact();});Grid.SetColumn(_collapseButton,3);header.Children.Add(_collapseButton);
        var close=HeaderButton("AnnotationsClose","M4,4 L14,14 M14,4 L4,14",_close);Grid.SetColumn(close,4);header.Children.Add(close);_root.Children.Add(header);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
        _tools=tools;
        _operate=Button("AnnotationsOperate",null,()=>_mode(false),!_drawing);tools.Children.Add(_operate);
        var icons=new[]{"M2,14 L12,4 L15,7 L5,17 L2,17 Z M10,6 L13,9","M2,14 L15,2","M2,14 L15,2 M7,2 L15,2 L15,10","M2,3 L15,3 L15,14 L2,14 Z","M8,2 C17,2 17,15 8,15 C-1,15 -1,2 8,2","M2,10 L9,3 Q10,2 11,3 L16,8 Q17,9 16,10 L11,15 L7,15 Z M6,6 L12,12 M7,15 L16,15"};
        foreach(var tool in Enum.GetValues<AnnotationTool>())
        {
            var captured=tool;
            var button=Button("Annotations"+tool,icons[(int)tool],()=>{_document.Tool=captured;_mode(true);},_drawing&&_document.Tool==tool);
            _toolButtons[tool]=button;tools.Children.Add(button);
        }
        _undo=Button("AnnotationsUndo","M6,3 L1,8 L6,13 M1,8 L10,8 Q16,8 16,15",_document.Undo);_undo.IsEnabled=_document.CanUndo;tools.Children.Add(_undo);
        _redo=Button("AnnotationsRedo","M10,3 L15,8 L10,13 M15,8 L6,8 Q0,8 0,15",_document.Redo);_redo.IsEnabled=_document.CanRedo;tools.Children.Add(_redo);
        _root.Children.Add(tools);
        var appearance=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
        _appearance=appearance;
        foreach(var pair in new[]{("Red",0xffe63946u),("Orange",0xfff28c28u),("Yellow",0xfff4c542u),("Green",0xff16a085u),("Blue",0xff2563ebu),("Black",0xff202a35u),("White",0xffffffffu)})
        {
            var b=Button("AnnotationsColor"+pair.Item1,null,()=>{_document.Color=pair.Item2;_style(_document.Color,_document.StrokeWidth);UpdateAppearanceSelection();});
            b.Width=30;b.MinWidth=30;b.Content=new Border{Width=16,Height=16,CornerRadius=new(8),Background=new SolidColorBrush(Color.FromUInt32(pair.Item2)),BorderBrush=Brush.Parse("#94A3B8"),BorderThickness=new(1)};
            _colorButtons[pair.Item2]=b;appearance.Children.Add(b);
        }
        foreach(var width in new[]{2d,4d,8d})
        {
            var b=Button("AnnotationsWidth",null,()=>{_document.StrokeWidth=width;_style(_document.Color,width);UpdateAppearanceSelection();},_document.StrokeWidth==width);
            b.Content=new Border{Width=18,Height=width,CornerRadius=new(2),Background=Brush.Parse("#334155")};
            ToolTip.SetTip(b,Strings["AnnotationsWidth"]+" "+width);ToolTip.SetShowOnDisabled(b,true);AutomationProperties.SetName(b,Strings["AnnotationsWidth"]+" "+width);_widthButtons[width]=b;appearance.Children.Add(b);
        }
        appearance.Children.Add(Button("AnnotationsClear",null,_document.Clear));_root.Children.Add(appearance);UpdateAppearanceSelection();
        _help=new TextBlock{Text=Strings["AnnotationsEsc"],FontSize=11,Foreground=Brush.Parse("#64748B"),TextWrapping=TextWrapping.Wrap};_root.Children.Add(_help);UpdateCompact();
    }
    private void UpdateCompact()
    {
        if(_tools is not null)_tools.IsVisible=!_compact;
        if(_appearance is not null)_appearance.IsVisible=!_compact;
        if(_help is not null)_help.IsVisible=!_compact;
        if(_collapseButton?.Content is Canvas canvas && canvas.Children[0] is Avalonia.Controls.Shapes.Path icon)
            icon.Data=Geometry.Parse(_compact?"M4,7 L9,12 L14,7":"M4,11 L9,6 L14,11");
        if(_collapseButton is not null)ToolTip.SetTip(_collapseButton,Strings[_compact?"AnnotationsExpandHint":"AnnotationsCollapseHint"]);
    }
    private void UpdateToolSelection()
    {
        if(_title is not null)_title.Text="⠿  "+Strings["AnnotationsTitle"]+" · "+Strings[_drawing?"AnnotationsDrawing":"AnnotationsOperating"];
        if(_hideButton is not null)_hideButton.IsVisible=_canHide?.Invoke()??true;
        if(_operate is not null)SetSelected(_operate,!_drawing);
        foreach(var (tool,button) in _toolButtons)SetSelected(button,_drawing&&_document.Tool==tool);
    }
    private static void SetSelected(Button button,bool selected)
    {
        button.BorderBrush=Brush.Parse(selected?"#3B82F6":"#E2E8F0");
        button.Background=Brush.Parse(selected?"#DBEAFE":"#F8FAFC");
    }
    private void UpdateAppearanceSelection()
    {
        foreach(var (color,button) in _colorButtons)
            button.BorderBrush=Brush.Parse(color==_document.Color?"#2563EB":"#E2E8F0");
        foreach(var (width,button) in _widthButtons)
        {
            var selected=width==_document.StrokeWidth;
            SetSelected(button,selected);
        }
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;
        _document.Changed-=Changed;Strings.PropertyChanged-=Changed;Close();
    }
}
