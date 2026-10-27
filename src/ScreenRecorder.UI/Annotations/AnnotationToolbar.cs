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
    private LanguageManager Strings=>LanguageManager.Instance;
    public AnnotationToolbar(AnnotationDocument document,Action<bool> mode,Action close,Action<uint,double> style,Action<Exception> failure,Action? minimize=null,Action? hide=null,Func<bool>? canHide=null)
    {
        _document=document;_mode=mode;_close=close;_style=style;_failure=failure;_minimize=minimize;_hide=hide;_canHide=canHide;
        Width=590;SizeToContent=SizeToContent.Height;CanResize=false;SystemDecorations=SystemDecorations.None;
        ShowInTaskbar=false;Topmost=true;ShowActivated=false;Background=Brushes.Transparent;
        TransparencyLevelHint=[WindowTransparencyLevel.Transparent];
        Content=new Border{Background=Brushes.White,BorderBrush=Brush.Parse("#CBD5E1"),BorderThickness=new(1),CornerRadius=new(12),Padding=new(12),Child=_root};
        document.Changed+=Changed;Strings.PropertyChanged+=Changed;
        Closed+=(_,_)=>Dispose();
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){Run(()=>_mode(false));e.Handled=true;}};
        Rebuild();
    }
    public void SetMode(bool drawing){_drawing=drawing;Rebuild();}
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
                new Avalonia.Controls.Shapes.Path{Data=Geometry.Parse(path),Width=17,Height=17,Stretch=Stretch.Uniform,
                    Stroke=Brush.Parse("#334155"),StrokeThickness=1.7,StrokeLineCap=PenLineCap.Round,StrokeJoin=PenLineJoin.Round}};
        ToolTip.SetTip(b,label);AutomationProperties.SetName(b,label);b.Click+=(_,_)=>Run(action);return b;
    }
    private void Run(Action action){try{action();}catch(Exception e){_failure(e);}}
    private void Rebuild()
    {
        Title=Strings["AnnotationsTitle"];
        _root.Children.Clear();
        var header=new Grid{ColumnDefinitions=new("*,Auto,Auto,Auto,Auto")};
        var title=new TextBlock{Text="⠿  "+Strings["AnnotationsTitle"]+" · "+Strings[_drawing?"AnnotationsDrawing":"AnnotationsOperating"],
            FontSize=12,FontWeight=FontWeight.SemiBold,Foreground=Brush.Parse("#334155"),VerticalAlignment=VerticalAlignment.Center};
        var dragArea=new Border{Background=Brushes.Transparent,Child=title};
        ToolTip.SetTip(dragArea,Strings["AnnotationsDrag"]);
        dragArea.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)Run(()=>BeginMoveDrag(e));};
        header.Children.Add(dragArea);
        if(_minimize is not null){var minimize=Button("AnnotationsMinimize","M3,11 L13,11",_minimize);Grid.SetColumn(minimize,1);header.Children.Add(minimize);}
        var canHide=_hide is not null&&(_canHide?.Invoke()??true);
        if(canHide){var hide=Button("AnnotationsHide","M2,8 C4,4 12,4 14,8 C12,12 4,12 2,8 Z M3,13 L13,3",_hide!);Grid.SetColumn(hide,2);header.Children.Add(hide);}
        var collapse=Button("AnnotationsCollapse","M3,8 L8,3 L13,8",()=>{_compact=!_compact;Rebuild();});Grid.SetColumn(collapse,3);header.Children.Add(collapse);
        var close=Button("AnnotationsClose","M3,3 L13,13 M13,3 L3,13",_close);Grid.SetColumn(close,4);header.Children.Add(close);_root.Children.Add(header);
        if(_compact)return;
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
        tools.Children.Add(Button("AnnotationsOperate",null,()=>_mode(false),!_drawing));
        var icons=new[]{"M2,14 L12,4 L15,7 L5,17 L2,17 Z M10,6 L13,9","M2,14 L15,2","M2,14 L15,2 M7,2 L15,2 L15,10","M2,3 L15,3 L15,14 L2,14 Z","M8,2 C17,2 17,15 8,15 C-1,15 -1,2 8,2","M2,10 L10,2 L16,8 L8,16 L2,10 M7,5 L13,11"};
        foreach(var tool in Enum.GetValues<AnnotationTool>())
        {
            var captured=tool;
            tools.Children.Add(Button("Annotations"+tool,icons[(int)tool],()=>{_document.Tool=captured;_mode(true);},_drawing&&_document.Tool==tool));
        }
        _undo=Button("AnnotationsUndo","M6,3 L1,8 L6,13 M1,8 L10,8 Q16,8 16,15",_document.Undo);_undo.IsEnabled=_document.CanUndo;tools.Children.Add(_undo);
        _redo=Button("AnnotationsRedo","M10,3 L15,8 L10,13 M15,8 L6,8 Q0,8 0,15",_document.Redo);_redo.IsEnabled=_document.CanRedo;tools.Children.Add(_redo);
        _root.Children.Add(tools);
        var appearance=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
        foreach(var pair in new[]{("Red",0xffe63946u),("Orange",0xfff28c28u),("Yellow",0xfff4c542u),("Green",0xff16a085u),("Blue",0xff2563ebu),("Black",0xff202a35u),("White",0xffffffffu)})
        {
            var b=Button("AnnotationsColor"+pair.Item1,null,()=>{_document.Color=pair.Item2;_style(_document.Color,_document.StrokeWidth);Rebuild();});
            b.Width=30;b.MinWidth=30;b.Content=new Border{Width=16,Height=16,CornerRadius=new(8),Background=new SolidColorBrush(Color.FromUInt32(pair.Item2)),BorderBrush=Brush.Parse("#94A3B8"),BorderThickness=new(1)};
            if(_document.Color==pair.Item2)b.BorderBrush=Brush.Parse("#2563EB");appearance.Children.Add(b);
        }
        foreach(var width in new[]{2d,4d,8d})
        {
            var b=Button("AnnotationsWidth",null,()=>{_document.StrokeWidth=width;_style(_document.Color,width);Rebuild();},_document.StrokeWidth==width);
            b.Content=new Border{Width=18,Height=width,CornerRadius=new(2),Background=Brush.Parse("#334155")};
            ToolTip.SetTip(b,Strings["AnnotationsWidth"]+" "+width);AutomationProperties.SetName(b,Strings["AnnotationsWidth"]+" "+width);appearance.Children.Add(b);
        }
        appearance.Children.Add(Button("AnnotationsClear",null,_document.Clear));_root.Children.Add(appearance);
        _root.Children.Add(new TextBlock{Text=Strings["AnnotationsEsc"]+(canHide?" · "+Strings["AnnotationsShortcut"]:""),FontSize=11,Foreground=Brush.Parse("#64748B"),TextWrapping=TextWrapping.Wrap});
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;
        _document.Changed-=Changed;Strings.PropertyChanged-=Changed;Close();
    }
}
