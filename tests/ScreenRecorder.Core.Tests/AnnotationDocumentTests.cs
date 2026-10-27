using ScreenRecorder.Core.Annotations;

namespace ScreenRecorder.Core.Tests;

public class AnnotationDocumentTests
{
    [Fact] public void HoverAndNonLeftPressDoNotDraw()
    {
        var d=new AnnotationDocument(800,600);
        d.Move(new(20,30));d.Begin(new(20,30),false);d.Move(new(50,60));d.End();
        Assert.Empty(d.Marks);Assert.Null(d.Preview);Assert.False(d.CanUndo);
    }
    [Theory]
    [InlineData(AnnotationTool.Pen)] [InlineData(AnnotationTool.Line)] [InlineData(AnnotationTool.Arrow)]
    [InlineData(AnnotationTool.Rectangle)] [InlineData(AnnotationTool.Ellipse)]
    public void OnlyPressedDragCreatesOneImmutableMark(AnnotationTool tool)
    {
        var d=new AnnotationDocument(800,600){Tool=tool,Color=0xff123456,StrokeWidth=6};
        d.Begin(new(100,120),true);d.Move(new(50,40));Assert.Empty(d.Marks);Assert.NotNull(d.Preview);
        d.End();var mark=Assert.Single(d.Marks);
        Assert.Equal(tool,mark.Tool);Assert.Equal(0xff123456u,mark.Color);Assert.Equal(6,mark.Width);
        Assert.Equal(new AnnotationPoint(50,40),mark.Points[^1]);
        var old=d.Marks;d.Clear();Assert.Single(old);Assert.True(d.CanUndo);d.Undo();Assert.Single(d.Marks);
    }
    [Fact] public void CancelPreservesExistingMarksAndDoesNotAddUndoStep()
    {
        var d=new AnnotationDocument(100,100);Stroke(d,10,20);
        d.Begin(new(5,5),true);d.Move(new(90,90));d.Cancel();
        Assert.Null(d.Preview);Assert.Single(d.Marks);d.Undo();Assert.Empty(d.Marks);
    }
    [Fact] public void UndoRedoAndClearAreReversibleButNewStrokeDropsRedo()
    {
        var d=new AnnotationDocument(100,100);Stroke(d,10,20);Stroke(d,30,40);
        d.Clear();Assert.Empty(d.Marks);d.Undo();Assert.Equal(2,d.Marks.Length);
        d.Undo();Assert.Single(d.Marks);d.Redo();Assert.Equal(2,d.Marks.Length);
        d.Undo();Stroke(d,50,60);Assert.False(d.CanRedo);Assert.Equal(2,d.Marks.Length);
    }
    [Fact] public void EraseRemovesWholeTopmostMarkOnlyAndCanUndo()
    {
        var d=new AnnotationDocument(100,100);Stroke(d,10,30);Stroke(d,10,30);
        var first=d.Marks[0];d.Tool=AnnotationTool.Remove;d.Begin(new(20,20),true);d.End();
        Assert.Equal(first,Assert.Single(d.Marks));d.Undo();Assert.Equal(2,d.Marks.Length);
        d.Begin(new(99,1),true);Assert.Equal(2,d.Marks.Length);
    }
    [Theory] [InlineData(AnnotationTool.Rectangle)] [InlineData(AnnotationTool.Ellipse)]
    public void ClosedShapesEraseAtBorderNotTheirEmptyInterior(AnnotationTool tool)
    {
        var d=new AnnotationDocument(200,200){Tool=tool};d.Begin(new(20,20),true);d.Move(new(180,180));d.End();
        Assert.False(d.RemoveAt(new(100,100)));Assert.True(d.RemoveAt(new(100,20)));Assert.Empty(d.Marks);
    }
    [Fact] public void InvalidCoordinatesAreRejectedAndCapturedMoveIsClipped()
    {
        var d=new AnnotationDocument(100,100);
        d.Begin(new(-1,3),true);Assert.Null(d.Preview);
        d.Begin(new(double.NaN,3),true);Assert.Null(d.Preview);
        d.Begin(new(3,3),true);d.Move(new(double.PositiveInfinity,4));d.Move(new(200,-5));d.End();
        Assert.Equal(new AnnotationPoint(100,0),d.Marks[0].Points[^1]);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new AnnotationDocument(0,100));
    }
    [Fact] public void PointsMarksAndUndoHistoryStayBounded()
    {
        var d=new AnnotationDocument(100,100);
        for(int i=0;i<AnnotationDocument.MaximumMarks+20;i++)Stroke(d,1,99);
        Assert.Equal(AnnotationDocument.MaximumMarks,d.Marks.Length);
        var count=0;while(d.CanUndo){d.Undo();count++;}
        Assert.InRange(count,1,AnnotationDocument.MaximumHistory);
        var longStroke=new AnnotationDocument(100,100);longStroke.Begin(new(1,1),true);
        for(int i=0;i<10000;i++)longStroke.Move(new(i%100,(i*17)%100));longStroke.End();
        Assert.InRange(longStroke.Marks[0].Points.Length,2,AnnotationDocument.MaximumStrokePoints);
    }
    static void Stroke(AnnotationDocument d,double from,double to)
    {d.Tool=AnnotationTool.Pen;d.Begin(new(from,from),true);d.Move(new(to,to));d.End();}
}
