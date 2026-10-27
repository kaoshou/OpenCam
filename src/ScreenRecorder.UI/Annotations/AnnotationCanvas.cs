// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ScreenRecorder.Core.Annotations;

namespace ScreenRecorder.UI.Annotations;

public sealed class AnnotationCanvas:Control,IDisposable
{
    public AnnotationDocument Document{get;}
    private IPointer? _pointer;
    private bool _interactive,_disposed;
    public bool Interactive{get=>_interactive;set{if(_interactive==value)return;CancelGesture();_interactive=value;}}
    public AnnotationCanvas(AnnotationDocument document)
    {
        Document=document;Focusable=true;ClipToBounds=true;
        document.Changed+=OnChanged;
        PointerPressed+=Pressed;PointerMoved+=Moved;PointerReleased+=Released;
        PointerCaptureLost+=(_,_)=>{_pointer=null;Document.Cancel();};
        DetachedFromVisualTree+=(_,_)=>CancelGesture();
    }
    private static AnnotationPoint Position(Point p)=>new(p.X,p.Y);
    private void Pressed(object? sender,PointerPressedEventArgs e)
    {
        if(!Interactive||_disposed||e.GetCurrentPoint(this).Properties.PointerUpdateKind!=PointerUpdateKind.LeftButtonPressed)return;
        Focus();Document.Begin(Position(e.GetPosition(this)),true);
        if(Document.IsDrawing){_pointer=e.Pointer;e.Pointer.Capture(this);}e.Handled=true;
    }
    private void Moved(object? sender,PointerEventArgs e)
    {
        if(!Interactive||!Document.IsDrawing)return;
        if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed){CancelGesture();return;}
        Document.Move(Position(e.GetPosition(this)));e.Handled=true;
    }
    private void Released(object? sender,PointerReleasedEventArgs e)
    {
        if(e.InitialPressMouseButton!=MouseButton.Left)return;
        if(Document.IsDrawing){Document.Move(Position(e.GetPosition(this)));Document.End();}
        ReleaseCapture();e.Handled=Interactive;
    }
    public void CancelGesture(){Document.Cancel();ReleaseCapture();}
    private void ReleaseCapture(){var pointer=_pointer;_pointer=null;pointer?.Capture(null);}
    private void OnChanged(object? sender,EventArgs e){if(!Document.IsDrawing)ReleaseCapture();InvalidateVisual();}
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Transparent hit-test surface; native input policy controls cross-application click-through.
        context.FillRectangle(Brushes.Transparent,new Rect(Bounds.Size));
        foreach(var mark in Document.Marks)Draw(context,mark);
        if(Document.Preview is { } preview)Draw(context,preview);
    }
    private static void Draw(DrawingContext dc,AnnotationMark m)
    {
        var brush=new SolidColorBrush(Color.FromUInt32(m.Color));
        var pen=new Pen(brush,m.Width,lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round);
        Point P(AnnotationPoint p)=>new(p.X,p.Y);
        var a=P(m.Points[0]);var b=P(m.Points[^1]);
        if(m.Tool==AnnotationTool.Pen)
        {
            if(m.Points.Length==1){dc.DrawEllipse(brush,null,a,m.Width/2,m.Width/2);return;}
            var geometry=new StreamGeometry();using(var g=geometry.Open())
            {g.BeginFigure(a,false);foreach(var p in m.Points.Skip(1))g.LineTo(P(p));g.EndFigure(false);}
            dc.DrawGeometry(null,pen,geometry);return;
        }
        if(m.Points.Length<2)return;
        var rect=new Rect(a,b).Normalize();
        if(m.Tool==AnnotationTool.Rectangle)dc.DrawRectangle(null,pen,rect);
        else if(m.Tool==AnnotationTool.Ellipse)dc.DrawEllipse(null,pen,rect);
        else
        {
            dc.DrawLine(pen,a,b);
            if(m.Tool==AnnotationTool.Arrow)
            {
                var angle=Math.Atan2(b.Y-a.Y,b.X-a.X);var size=Math.Max(12,m.Width*3);
                foreach(var offset in new[]{-.5,.5})dc.DrawLine(pen,b,new(b.X-size*Math.Cos(angle+offset),b.Y-size*Math.Sin(angle+offset)));
            }
        }
    }
    public void Dispose(){if(_disposed)return;_disposed=true;CancelGesture();Document.Changed-=OnChanged;}
}
