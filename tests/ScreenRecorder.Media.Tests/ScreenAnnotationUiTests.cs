using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Automation;
using Avalonia.VisualTree;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.Core.Annotations;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.Annotations;

namespace ScreenRecorder.Media.Tests;

public class ScreenAnnotationUiTests
{
    private static readonly CaptureRegion Region=new(-1000,20,800,600);
    [Fact] public void ControllerIsLazyAndDisabledFeatureNeverCreatesHost()
    {
        var made=0;using var controller=new ScreenAnnotationController(_=>{made++;return new Host();});
        controller.Update(false,true,false,Region);controller.Open();
        controller.Update(true,false,false,Region);controller.Open();Assert.Equal(0,made);
        controller.Update(true,true,false,Region);controller.Open();controller.Open();Assert.Equal(1,made);
    }
    [Fact] public void PausePreservesDocumentButStopDisableAndRangeChangeDispose()
    {
        var host=new Host();using var controller=new ScreenAnnotationController(_=>host);
        controller.Update(true,true,false,Region);controller.Open();
        host.Document.Begin(new(1,1),true);host.Document.Move(new(10,10));host.Document.End();
        host.Document.Begin(new(2,2),true);
        controller.Update(true,false,true,Region);Assert.Null(host.Document.Preview);Assert.True(host.Paused);Assert.Single(host.Document.Marks);
        controller.Update(true,true,false,Region);Assert.False(host.Paused);
        controller.Update(true,true,false,Region with{X=100});Assert.True(host.Disposed);Assert.False(controller.IsOpen);
        controller.Open();controller.Update(false,true,false,Region with{X=100});Assert.False(controller.IsOpen);
    }
    [Fact] public void OptionalWindowFailureIsReportedAndClosesWithoutEscaping()
    {
        var host=new Host{FailShow=true};using var controller=new ScreenAnnotationController(_=>host);Exception? error=null;
        controller.Failed+=(_,e)=>error=e;controller.Update(true,true,false,Region);controller.Open();
        Assert.NotNull(error);Assert.True(host.Disposed);Assert.False(controller.IsOpen);
    }
    [Fact] public void InvalidRegionNeverBuildsNativeWindows()
    {
        using var controller=new ScreenAnnotationController(_=>throw new Exception("Must not create"));
        controller.Update(true,true,false,new(0,0,0,100));controller.Open();Assert.False(controller.IsOpen);
    }
    [AvaloniaFact] public void CanvasHoverDoesNotDrawAndLeftDragLeavesCursorUnchanged()
    {
        var doc=new AnnotationDocument(400,300);var canvas=new AnnotationCanvas(doc){Interactive=true};
        var window=new Window{Width=400,Height=300,Content=canvas};
        try{
            window.Show();window.UpdateLayout();var cursor=canvas.Cursor;
            window.MouseMove(new(10,10));window.MouseMove(new(30,30));Assert.Empty(doc.Marks);
            window.MouseDown(new(20,20),MouseButton.Right);window.MouseUp(new(20,20),MouseButton.Right);Assert.Empty(doc.Marks);
            window.MouseDown(new(20,20),MouseButton.Left);Assert.True(doc.IsDrawing);
            window.MouseMove(new(80,80),RawInputModifiers.LeftMouseButton);window.MouseUp(new(80,80),MouseButton.Left);
            Assert.Single(doc.Marks);Assert.Same(cursor,canvas.Cursor);
            canvas.Interactive=false;window.MouseDown(new(50,50),MouseButton.Left);window.MouseUp(new(80,80),MouseButton.Left);Assert.Single(doc.Marks);
        }finally{window.MouseUp(new(80,80),MouseButton.Left);window.Close();canvas.Dispose();}
    }
    [AvaloniaFact] public void ChangingModeCancelsCapturedGestureAndUnsubscribesOnDispose()
    {
        var doc=new AnnotationDocument(400,300);var canvas=new AnnotationCanvas(doc){Interactive=true};
        var window=new Window{Width=400,Height=300,Content=canvas};
        try{window.Show();window.UpdateLayout();window.MouseDown(new(20,20),MouseButton.Left);
            Assert.True(doc.IsDrawing);canvas.Interactive=false;Assert.False(doc.IsDrawing);Assert.Empty(doc.Marks);
            canvas.Dispose();doc.Clear();Assert.Null(canvas.Cursor);
        }finally{window.MouseUp(new(20,20),MouseButton.Left);window.Close();canvas.Dispose();}
    }
    [AvaloniaTheory] [InlineData(AppLanguage.ZhTw)] [InlineData(AppLanguage.EnUs)] [InlineData(AppLanguage.JaJp)]
    public void ToolbarKeepsControlsStableDuringDrawingAndEveryButtonHasLocalizedName(AppLanguage language)
    {
        var old=LanguageManager.Instance.CurrentLanguage;LanguageManager.Instance.CurrentLanguage=language;
        var doc=new AnnotationDocument(800,600);var toolbar=new AnnotationToolbar(doc,_=>{},()=>{},(_,_)=>{},e=>throw e);
        try{toolbar.Show();toolbar.UpdateLayout();
            var buttons=toolbar.GetVisualDescendants().OfType<Button>().ToArray();Assert.True(buttons.Length>=20);
            Assert.All(buttons,b=>{var name=AutomationProperties.GetName(b);Assert.False(string.IsNullOrWhiteSpace(name));Assert.DoesNotContain("Annotations",name);});
            doc.Begin(new(10,10),true);for(int i=11;i<100;i++)doc.Move(new(i,i));doc.End();
            Assert.Equal(buttons,toolbar.GetVisualDescendants().OfType<Button>().ToArray());
            Assert.All(buttons,b=>Assert.True(b.Bounds.Right<=toolbar.ClientSize.Width));
        }finally{toolbar.Close();LanguageManager.Instance.CurrentLanguage=old;}
    }
    [AvaloniaFact] public void LostButtonStateCancelsRatherThanLeavingAPhantomStroke()
    {
        var doc=new AnnotationDocument(400,300);var canvas=new AnnotationCanvas(doc){Interactive=true};
        var window=new Window{Width=400,Height=300,Content=canvas};
        try{window.Show();window.UpdateLayout();window.MouseDown(new(20,20),MouseButton.Left);
            window.MouseMove(new(40,40),RawInputModifiers.None);Assert.False(doc.IsDrawing);Assert.Empty(doc.Marks);
        }finally{window.MouseUp(new(40,40),MouseButton.Left);window.Close();canvas.Dispose();}
    }
    private sealed class Host:IAnnotationHost
    {
        public AnnotationDocument Document{get;}=new(800,600);
        public bool Disposed,Paused,FailShow;
        public event EventHandler? Closed;
        public void Show(){if(FailShow)throw new InvalidOperationException("Native window unavailable");}
        public void SetPaused(bool paused){Paused=paused;Document.Cancel();}
        public void Dispose(){Disposed=true;Closed?.Invoke(this,EventArgs.Empty);}
    }
}
