// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.Services;
using System.Runtime.InteropServices;

namespace ScreenRecorder.UI.Views;

// Source-local crop dialog: never uses XWayland global screen coordinates.
public sealed class LinuxPortalCropWindow : Window
{
    private readonly LinuxPortalSelectionView source;
    private readonly Canvas canvas;
    private readonly Rectangle outline;
    private readonly TextBlock summary;
    private readonly Button confirm;
    private Point? anchor;
    public CaptureRegion SelectedRegion { get; private set; }
    public LinuxPortalCropWindow(LinuxPortalSelectionView view, CaptureRegion initial)
    {
        source=view;
        if (view.Width is <2 or >16384 || view.Height is <2 or >16384 || view.PreviewWidth is <1 or >480 || view.PreviewHeight is <1 or >270)
            throw new ArgumentException("Invalid source thumbnail.");
        var bytes=Convert.FromBase64String(view.PreviewRgba);
        if (bytes.Length!=view.PreviewWidth*view.PreviewHeight*4) throw new ArgumentException("Invalid source thumbnail.");
        var text=LanguageManager.Instance;
        Title=text["LinuxPortalCropTitle"]; Width=680; Height=470; MinWidth=440; MinHeight=360;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=Brush.Parse("#F8FAFC");
        var bitmap=new WriteableBitmap(new(view.PreviewWidth,view.PreviewHeight),new(96,96),PixelFormat.Rgba8888,AlphaFormat.Premul);
        using(var target=bitmap.Lock())
            for(var row=0;row<view.PreviewHeight;row++)
                Marshal.Copy(bytes,row*view.PreviewWidth*4,target.Address+row*target.RowBytes,view.PreviewWidth*4);
        Closed+=(_,_)=>bitmap.Dispose();
        canvas=new Canvas {Width=view.PreviewWidth,Height=view.PreviewHeight,Background=Brushes.Transparent};
        outline=new Rectangle {Stroke=Brushes.Orange,StrokeThickness=2,Fill=new SolidColorBrush(Color.FromArgb(35,255,170,0)),IsHitTestVisible=false};
        canvas.Children.Add(outline);
        var preview=new Grid {Width=view.PreviewWidth,Height=view.PreviewHeight};
        preview.Children.Add(new Image {Source=bitmap,Stretch=Stretch.Fill}); preview.Children.Add(canvas);
        var pane=new Grid {RowDefinitions=new("Auto,*,Auto"),Margin=new(20)};
        pane.Children.Add(new TextBlock {Text=text["LinuxPortalCropHint"],TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#334155")});
        var picture=new Border {Margin=new(0,14),Background=Brushes.Black,CornerRadius=new(8),Child=new Viewbox {Stretch=Stretch.Uniform,Child=preview}};
        Grid.SetRow(picture,1); pane.Children.Add(picture);
        var footer=new Grid {ColumnDefinitions=new("*,Auto,Auto,Auto")};
        summary=new TextBlock {VerticalAlignment=VerticalAlignment.Center};footer.Children.Add(summary);
        var reset=new Button {Content=text["LinuxPortalFullSource"],Margin=new(4,0)};Grid.SetColumn(reset,1); footer.Children.Add(reset);
        var cancel=new Button {Content=text["Cancel"],Margin=new(4,0)};Grid.SetColumn(cancel,2);footer.Children.Add(cancel);
        confirm=new Button {Content=text["Confirm"],Margin=new(4,0),Background=Brush.Parse("#0284C7"),Foreground=Brushes.White};Grid.SetColumn(confirm,3);footer.Children.Add(confirm);
        Grid.SetRow(footer,2);pane.Children.Add(footer);Content=pane;
        SelectedRegion=initial.X>=0&&initial.Y>=0&&initial.Width>=2&&initial.Height>=2&&
            (long)initial.X+initial.Width<=view.Width&&(long)initial.Y+initial.Height<=view.Height
            ? initial with {Width=initial.Width&~1,Height=initial.Height&~1} : Full();
        reset.Click+=(_,_)=>{SelectedRegion=Full();Refresh();};
        cancel.Click+=(_,_)=>Close(false);confirm.Click+=(_,_)=>Close(true);
        canvas.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed){anchor=e.GetPosition(canvas);e.Pointer.Capture(canvas);e.Handled=true;}};
        canvas.PointerMoved+=(_,e)=>{if(anchor is {} start){Update(start,e.GetPosition(canvas));e.Handled=true;}};
        canvas.PointerReleased+=(_,e)=>{if(anchor is {} start){Update(start,e.GetPosition(canvas));anchor=null;e.Pointer.Capture(null);e.Handled=true;}};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close(false);e.Handled=true;}};
        Refresh();
    }
    private CaptureRegion Full()=>new(0,0,source.Width&~1,source.Height&~1);
    private void Update(Point start,Point end){SelectedRegion=LinuxPortalCropGeometry.MapDrag(source.Width,source.Height,source.PreviewWidth,source.PreviewHeight,start.X,start.Y,end.X,end.Y);Refresh();}
    private void Refresh()
    {
        var r=SelectedRegion;summary.Text=$"{r.X}, {r.Y} · {r.Width} × {r.Height}";confirm.IsEnabled=r.Width>=2&&r.Height>=2;
        Canvas.SetLeft(outline,(double)r.X*source.PreviewWidth/source.Width);Canvas.SetTop(outline,(double)r.Y*source.PreviewHeight/source.Height);
        outline.Width=(double)r.Width*source.PreviewWidth/source.Width;outline.Height=(double)r.Height*source.PreviewHeight/source.Height;
    }
}
