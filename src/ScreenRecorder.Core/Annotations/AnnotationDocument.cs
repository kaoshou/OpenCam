// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;

namespace ScreenRecorder.Core.Annotations;

public enum AnnotationTool { Pen, Line, Arrow, Rectangle, Ellipse, Remove }
public readonly record struct AnnotationPoint(double X,double Y)
{
    public bool IsFinite => double.IsFinite(X)&&double.IsFinite(Y);
}
public sealed record AnnotationMark(AnnotationTool Tool,uint Color,double Width,ImmutableArray<AnnotationPoint> Points);

/// <summary>Ephemeral, bounded vector document. No recording, project, window or filesystem dependencies.</summary>
public sealed class AnnotationDocument
{
    public const int MaximumMarks=256,MaximumHistory=32,MaximumStrokePoints=2048,MaximumDocumentPoints=32768;
    private readonly double _width,_height;
    private readonly List<ImmutableArray<AnnotationMark>> _undo=[],_redo=[];
    private List<AnnotationPoint>? _pending;
    private AnnotationTool _gestureTool;
    private uint _gestureColor;
    private double _gestureWidth;
    private AnnotationTool _tool;
    public AnnotationTool Tool { get=>_tool;set {if(!Enum.IsDefined(value))throw new ArgumentOutOfRangeException(nameof(value));Cancel();_tool=value;} }
    public uint Color {get;set;}=0xffe63946;
    public double StrokeWidth {get;set;}=4;
    public ImmutableArray<AnnotationMark> Marks {get;private set;}=[];
    public AnnotationMark? Preview=>_pending is null?null:new(_gestureTool,_gestureColor,_gestureWidth,_pending.ToImmutableArray());
    public bool IsDrawing=>_pending is not null;
    public bool CanUndo=>_undo.Count>0;
    public bool CanRedo=>_redo.Count>0;
    public event EventHandler? Changed;

    public AnnotationDocument(double width,double height)
    {
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0||width>32768||height>32768)
            throw new ArgumentOutOfRangeException(nameof(width));
        _width=width;_height=height;
    }
    public void Begin(AnnotationPoint point,bool leftButton)
    {
        Cancel();
        if(!leftButton||!point.IsFinite||point.X<0||point.Y<0||point.X>_width||point.Y>_height)return;
        if(Tool==AnnotationTool.Remove){RemoveAt(point);return;}
        if(Marks.Length>=MaximumMarks||Marks.Sum(x=>x.Points.Length)>=MaximumDocumentPoints)return;
        _gestureTool=Tool;_gestureColor=Color|0xff000000;
        _gestureWidth=double.IsFinite(StrokeWidth)?Math.Clamp(StrokeWidth,1,24):4;
        _pending=[point];Notify();
    }
    public void Move(AnnotationPoint point)
    {
        if(_pending is null||!point.IsFinite)return;
        point=new(Math.Clamp(point.X,0,_width),Math.Clamp(point.Y,0,_height));
        var last=_pending[^1];
        if(DistanceSquared(point,last)<.25)return;
        if(_gestureTool!=AnnotationTool.Pen)
        {if(_pending.Count==1)_pending.Add(point);else _pending[1]=point;}
        else
        {
            if(_pending.Count>=MaximumStrokePoints)
                _pending=_pending.Where((_,i)=>i%2==0).ToList();
            _pending.Add(point);
        }
        Notify();
    }
    public void End()
    {
        var mark=Preview;_pending=null;
        if(mark is not null&&(mark.Tool==AnnotationTool.Pen||mark.Points.Length==2)&&
            Marks.Sum(x=>x.Points.Length)+mark.Points.Length<=MaximumDocumentPoints)
            Commit(Marks.Add(mark));
        else Notify();
    }
    public void Cancel(){if(_pending is null)return;_pending=null;Notify();}
    public void Clear(){Cancel();if(!Marks.IsEmpty)Commit([]);}
    public void Undo(){Cancel();if(_undo.Count==0)return;Push(_redo,Marks);Marks=Pop(_undo);Notify();}
    public void Redo(){Cancel();if(_redo.Count==0)return;Push(_undo,Marks);Marks=Pop(_redo);Notify();}
    public bool RemoveAt(AnnotationPoint point)
    {
        if(!point.IsFinite)return false;
        for(int i=Marks.Length-1;i>=0;i--)
            if(Hit(Marks[i],point)){Commit(Marks.RemoveAt(i));return true;}
        return false;
    }
    private void Commit(ImmutableArray<AnnotationMark> next){Push(_undo,Marks);_redo.Clear();Marks=next;Notify();}
    private static void Push(List<ImmutableArray<AnnotationMark>> stack,ImmutableArray<AnnotationMark> marks)
    {if(stack.Count==MaximumHistory)stack.RemoveAt(0);stack.Add(marks);}
    private static ImmutableArray<AnnotationMark> Pop(List<ImmutableArray<AnnotationMark>> stack)
    {var result=stack[^1];stack.RemoveAt(stack.Count-1);return result;}
    private void Notify()=>Changed?.Invoke(this,EventArgs.Empty);
    private static double DistanceSquared(AnnotationPoint a,AnnotationPoint b)=>Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2);
    private static double SegmentDistance(AnnotationPoint p,AnnotationPoint a,AnnotationPoint b)
    {
        var length=DistanceSquared(a,b);var t=length==0?0:Math.Clamp(((p.X-a.X)*(b.X-a.X)+(p.Y-a.Y)*(b.Y-a.Y))/length,0,1);
        return Math.Sqrt(DistanceSquared(p,new(a.X+t*(b.X-a.X),a.Y+t*(b.Y-a.Y))));
    }
    private static bool Hit(AnnotationMark mark,AnnotationPoint p)
    {
        var tolerance=6+mark.Width/2;var a=mark.Points[0];var b=mark.Points[^1];
        if(mark.Tool==AnnotationTool.Rectangle)
            return new[]{SegmentDistance(p,a,new(b.X,a.Y)),SegmentDistance(p,new(b.X,a.Y),b),
                SegmentDistance(p,b,new(a.X,b.Y)),SegmentDistance(p,new(a.X,b.Y),a)}.Min()<=tolerance;
        if(mark.Tool==AnnotationTool.Ellipse)
        {
            var rx=Math.Abs(b.X-a.X)/2;var ry=Math.Abs(b.Y-a.Y)/2;
            if(rx<.01||ry<.01)return SegmentDistance(p,a,b)<=tolerance;
            var cx=(a.X+b.X)/2;var cy=(a.Y+b.Y)/2;
            return Math.Abs(Math.Sqrt(Math.Pow((p.X-cx)/rx,2)+Math.Pow((p.Y-cy)/ry,2))-1)*Math.Min(rx,ry)<=tolerance;
        }
        if(mark.Points.Length==1)return Math.Sqrt(DistanceSquared(a,p))<=tolerance;
        for(int i=1;i<mark.Points.Length;i++)if(SegmentDistance(p,mark.Points[i-1],mark.Points[i])<=tolerance)return true;
        if(mark.Tool==AnnotationTool.Arrow)
        {
            var angle=Math.Atan2(b.Y-a.Y,b.X-a.X);var size=Math.Max(12,mark.Width*3);
            foreach(var offset in new[]{-.5,.5})
                if(SegmentDistance(p,b,new(b.X-size*Math.Cos(angle+offset),b.Y-size*Math.Sin(angle+offset)))<=tolerance)return true;
        }
        return false;
    }
}
