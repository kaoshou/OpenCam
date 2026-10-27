// SPDX-License-Identifier: AGPL-3.0-or-later
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
    [Fact] public void OpeningAgainTogglesToolbarWithoutDiscardingMarks()
    {
        var host=new Host();using var controller=new ScreenAnnotationController(_=>host);
        controller.Update(true,true,false,Region);controller.Open();
        host.Document.Begin(new(10,10),true);host.Document.Move(new(20,20));host.Document.End();
        controller.Open();Assert.Equal(1,host.Toggles);Assert.Single(host.Document.Marks);Assert.True(controller.IsOpen);
        controller.Open();Assert.Equal(2,host.Toggles);Assert.Single(host.Document.Marks);
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
            Assert.All(buttons,b=>{var position=b.TranslatePoint(default,toolbar)!.Value;Assert.True(position.X+b.Bounds.Width<=toolbar.ClientSize.Width+0.1);});
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
    [AvaloniaFact] public void OpenGeometryIconsHaveVisibleStrokesInsteadOfEmptyOrSolidFills()
    {
        using var toolbar=new AnnotationToolbar(new(800,600),_=>{},()=>{},(_,_)=>{},e=>throw e);
        toolbar.Show();toolbar.UpdateLayout();
        Assert.Empty(toolbar.GetVisualDescendants().OfType<PathIcon>());
        var paths=toolbar.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToArray();
        Assert.True(paths.Length>=10);
        Assert.All(paths,path=>{Assert.NotNull(path.Stroke);Assert.True(path.StrokeThickness>=1.5);});
    }
    [AvaloniaFact] public void RepeatedHideAndShowKeepsToolbarWidthAndControlsVisible()
    {
        using var toolbar=new AnnotationToolbar(new(800,600),_=>{},()=>{},(_,_)=>{},e=>throw e,null,()=>{},()=>true);
        toolbar.Show();toolbar.UpdateLayout();var width=toolbar.ClientSize.Width;
        for(var i=0;i<8;i++)
        {
            toolbar.Hide();toolbar.Show();toolbar.UpdateLayout();
            Assert.Equal(width,toolbar.ClientSize.Width,1);
            Assert.All(toolbar.GetVisualDescendants().OfType<Button>(),b=>{var position=b.TranslatePoint(default,toolbar)!.Value;Assert.True(position.X+b.Bounds.Width<=toolbar.ClientSize.Width+0.1);});
        }
        Assert.Equal(toolbar.Width,toolbar.MinWidth);
    }
    [AvaloniaFact] public void ToolbarRestoresFullHeightAfterDisplayResizeAndRepeatedHideShow()
    {
        using var toolbar=new AnnotationToolbar(new(800,600),_=>{},()=>{},(_,_)=>{},e=>throw e,null,()=>{},()=>true);
        toolbar.ShowToolbar();toolbar.UpdateLayout();Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var fullHeight=toolbar.ClientSize.Height;
        Assert.True(fullHeight<400,$"initial client={fullHeight}, property={toolbar.Height}, desired={((Control)toolbar.Content!).DesiredSize.Height}");
        for(var i=0;i<4;i++)
        {
            typeof(Avalonia.Platform.IWindowImpl).GetMethod("Resize",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)!
                .Invoke(toolbar.PlatformImpl,new object[]{new Avalonia.Size(toolbar.ClientSize.Width,fullHeight/2),WindowResizeReason.DpiChange});
            toolbar.Hide();toolbar.ShowToolbar();toolbar.UpdateLayout();Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(toolbar.ClientSize.Height>=fullHeight-1,$"Toolbar height {toolbar.ClientSize.Height} is less than {fullHeight}; height property {toolbar.Height}; desired {((Control)toolbar.Content!).DesiredSize.Height}");
            Assert.All(toolbar.GetVisualDescendants().OfType<Button>().Where(b=>b.IsVisible),b=>
            {var point=b.TranslatePoint(default,toolbar)!.Value;Assert.True(point.Y+b.Bounds.Height<=toolbar.ClientSize.Height+0.1);});
        }
        var collapse=toolbar.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance["AnnotationsCollapse"]);
        collapse.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        toolbar.UpdateLayout();Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(toolbar.ClientSize.Height<fullHeight);
        toolbar.Hide();toolbar.ShowToolbar();toolbar.UpdateLayout();Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        collapse.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        toolbar.UpdateLayout();Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(toolbar.ClientSize.Height>=fullHeight-1);
    }
    [AvaloniaFact] public void ToolbarIconsUseFixedCanvasRatherThanDegenerateStretchBounds()
    {
        using var toolbar=new AnnotationToolbar(new(800,600),_=>{},()=>{},(_,_)=>{},e=>throw e,()=>{},()=>{});
        toolbar.Show();toolbar.UpdateLayout();
        var minimize=toolbar.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance["AnnotationsMinimize"]);
        Assert.IsType<Canvas>(minimize.Content);
    }
    [AvaloniaFact] public void HeaderActionsUseSpacedConsistentIconViewports()
    {
        using var toolbar=new AnnotationToolbar(new(800,600),_=>{},()=>{},(_,_)=>{},e=>throw e,()=>{},()=>{},()=>true);
        toolbar.Show();toolbar.UpdateLayout();
        foreach(var key in new[]{"AnnotationsMinimize","AnnotationsHide","AnnotationsCollapse","AnnotationsClose"})
        {
            var button=toolbar.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance[key]);
            var canvas=Assert.IsType<Canvas>(button.Content);
            var path=Assert.IsType<Avalonia.Controls.Shapes.Path>(Assert.Single(canvas.Children));
            Assert.Equal(18,path.Width);Assert.Equal(18,path.Height);Assert.True(button.Margin.Left>=3);
        }
    }
    [AvaloniaTheory] [InlineData(AppLanguage.ZhTw)] [InlineData(AppLanguage.EnUs)] [InlineData(AppLanguage.JaJp)]
    public void ToolbarButtonsExposeLocalizedHoverTextAndCollapseHintChangesWithState(AppLanguage language)
    {
        var old=LanguageManager.Instance.CurrentLanguage;LanguageManager.Instance.CurrentLanguage=language;
        using var toolbar=new AnnotationToolbar(new(800,600),_=>{},()=>{},(_,_)=>{},e=>throw e,()=>{},()=>{},()=>true);
        try
        {
            toolbar.Show();toolbar.UpdateLayout();
            var buttons=toolbar.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.All(buttons,b=>{var tip=Assert.IsType<string>(ToolTip.GetTip(b));Assert.False(string.IsNullOrWhiteSpace(tip));Assert.DoesNotContain("Annotations",tip);Assert.True(ToolTip.GetShowOnDisabled(b));});
            var collapse=buttons.Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance["AnnotationsCollapse"]);
            Assert.Equal(LanguageManager.Instance["AnnotationsCollapseHint"],ToolTip.GetTip(collapse));
            collapse.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(LanguageManager.Instance["AnnotationsExpandHint"],ToolTip.GetTip(collapse));
        }
        finally {toolbar.Close();LanguageManager.Instance.CurrentLanguage=old;}
    }
    [AvaloniaFact] public void EscapeEndsDrawingAndClearsMarks()
    {
        var doc=new AnnotationDocument(800,600);bool? mode=null;
        doc.Begin(new(10,10),true);doc.Move(new(20,20));doc.End();
        using var toolbar=new AnnotationToolbar(doc,value=>mode=value,()=>{},(_,_)=>{},e=>throw e);
        toolbar.Show();toolbar.UpdateLayout();toolbar.KeyPress(Key.Escape,RawInputModifiers.None);
        Assert.False(mode);Assert.Empty(doc.Marks);
    }
    [AvaloniaFact] public void ConfiguredFinishKeyReplacesEscape()
    {
        var doc=new AnnotationDocument(800,600);bool? mode=null;
        doc.Begin(new(10,10),true);doc.Move(new(20,20));doc.End();
        using var toolbar=new AnnotationToolbar(doc,value=>mode=value,()=>{},(_,_)=>{},e=>throw e,finishShortcut:(k,m)=>k==Key.F7&&m==KeyModifiers.None);
        toolbar.Show();toolbar.UpdateLayout();toolbar.KeyPress(Key.Escape,RawInputModifiers.None);
        Assert.Single(doc.Marks);toolbar.KeyPress(Key.F7,RawInputModifiers.None);
        Assert.False(mode);Assert.Empty(doc.Marks);
    }
    [AvaloniaFact] public void RepeatedWidthChangesRemainClickableWithoutCreatingMarks()
    {
        var doc=new AnnotationDocument(800,600);
        using var toolbar=new AnnotationToolbar(doc,_=>{},()=>{},(_,_)=>{},e=>throw e);
        toolbar.Show();toolbar.UpdateLayout();
        foreach(var width in new[]{2,8,4,2,8,4})
        {
            var name=LanguageManager.Instance["AnnotationsWidth"]+" "+width;
            var button=toolbar.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name);
            var point=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),toolbar)!.Value;
            toolbar.MouseDown(point,MouseButton.Left);toolbar.MouseUp(point,MouseButton.Left);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();toolbar.UpdateLayout();
            Assert.Equal(width,doc.StrokeWidth);Assert.Empty(doc.Marks);
        }
    }
    [AvaloniaFact] public void RepeatedToolChangesDoNotReplaceButtons()
    {
        var doc=new AnnotationDocument(800,600);AnnotationToolbar? toolbar=null;
        toolbar=new AnnotationToolbar(doc,drawing=>toolbar!.SetMode(drawing),()=>{},(_,_)=>{},e=>throw e);
        using(toolbar)
        {
            toolbar.Show();toolbar.UpdateLayout();var before=toolbar.GetVisualDescendants().OfType<Button>().ToArray();
            foreach(var tool in new[]{AnnotationTool.Rectangle,AnnotationTool.Ellipse,AnnotationTool.Line,AnnotationTool.Pen})
            {
                var button=before.Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance["Annotations"+tool]);
                var point=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),toolbar)!.Value;
                toolbar.MouseDown(point,MouseButton.Left);toolbar.MouseUp(point,MouseButton.Left);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();toolbar.UpdateLayout();Assert.Equal(tool,doc.Tool);
                Assert.Equal(before,toolbar.GetVisualDescendants().OfType<Button>().ToArray());
            }
        }
    }
    [AvaloniaFact] public void EraserDragRemovesEveryTouchedMarkAndUndoRestoresGesture()
    {
        var doc=new AnnotationDocument(400,300);
        foreach(var x in new[]{50,100,150}) { doc.Begin(new(x,40),true);doc.Move(new(x,100));doc.End(); }
        doc.Tool=AnnotationTool.Remove;
        var canvas=new AnnotationCanvas(doc){Interactive=true};var window=new Window{Width=400,Height=300,Content=canvas};
        try { window.Show();window.UpdateLayout();window.MouseDown(new(20,70),MouseButton.Left);
            window.MouseMove(new(180,70),RawInputModifiers.LeftMouseButton);window.MouseUp(new(180,70),MouseButton.Left);
            Assert.Empty(doc.Marks);doc.Undo();Assert.Equal(3,doc.Marks.Length);
        } finally { window.Close();canvas.Dispose(); }
    }
    [AvaloniaFact] public void ToolbarExposesMinimizeAndHideActions()
    {
        var minimized=0;var hidden=0;using var toolbar=new AnnotationToolbar(new(800,600),_=>{},()=>{},(_,_)=>{},e=>throw e,()=>minimized++,()=>hidden++);
        toolbar.Show();toolbar.UpdateLayout();
        var buttons=toolbar.GetVisualDescendants().OfType<Button>().ToArray();
        buttons.Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance["AnnotationsMinimize"]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        buttons.Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance["AnnotationsHide"]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1,minimized);Assert.Equal(1,hidden);
    }
    [AvaloniaFact] public void ToolbarHidesItsHideActionWhenShortcutIsUnavailable()
    {
        var available=true;using var toolbar=new AnnotationToolbar(new(800,600),_=>{},()=>{},(_,_)=>{},e=>throw e,null,()=>{},()=>available);
        toolbar.Show();toolbar.UpdateLayout();
        Assert.Contains(toolbar.GetVisualDescendants().OfType<Button>(),b=>AutomationProperties.GetName(b)==LanguageManager.Instance["AnnotationsHide"]);
        available=false;toolbar.SetMode(false);toolbar.UpdateLayout();
        Assert.False(toolbar.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance["AnnotationsHide"]).IsVisible);
    }
    [AvaloniaFact] public void OpeningToolsDoesNotCoverDesktopUntilDrawingIsSelected()
    {
        var owner=new Window{Width=400,Height=300};owner.Show();owner.UpdateLayout();
        var screen=owner.Screens.ScreenFromWindow(owner)!;
        var region=new CaptureRegion(screen.Bounds.X+20,screen.Bounds.Y+20,200,150);
        using var host=new AnnotationOverlayHost(owner,region,0xffe63946,4,(_,_)=>{},()=>true,(_,_)=>true);
        try
        {
            host.Show();
            var overlay=(Window)typeof(AnnotationOverlayHost).GetField("_overlay",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(host)!;
            var toolbar=(Window)typeof(AnnotationOverlayHost).GetField("_toolbar",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(host)!;
            Assert.False(overlay.IsVisible);Assert.True(toolbar.IsVisible);
            toolbar.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance["AnnotationsPen"]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(overlay.IsVisible);
            toolbar.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance["AnnotationsOperate"]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(overlay.IsVisible);Assert.True(toolbar.IsVisible);
            toolbar.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance["AnnotationsPen"]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            host.Document.Begin(new(10,10),true);host.Document.Move(new(30,30));host.Document.End();
            host.TogglePen();Assert.Single(host.Document.Marks);host.TogglePen();Assert.Single(host.Document.Marks);
            toolbar.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)==LanguageManager.Instance["AnnotationsOperate"]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(overlay.IsVisible);Assert.Single(host.Document.Marks);
            host.SetPaused(true);Assert.False(overlay.IsVisible);host.SetPaused(false);
            Assert.True(overlay.IsVisible);Assert.Single(host.Document.Marks);
            host.Document.Clear();Assert.False(overlay.IsVisible);
            host.Document.Undo();Assert.True(overlay.IsVisible);Assert.Single(host.Document.Marks);
        }
        finally{host.Dispose();owner.Close();}
    }
    private sealed class Host:IAnnotationHost
    {
        public AnnotationDocument Document{get;}=new(800,600);
        public bool Disposed,Paused,FailShow;
        public int Toggles;
        public event EventHandler? Closed;
        public void Show(){if(FailShow)throw new InvalidOperationException("Native window unavailable");}
        public void ToggleToolbar()=>Toggles++;
        public void TogglePen() { }
        public void SelectTool(AnnotationTool tool) => Document.Tool=tool;
        public void FinishDrawing() => Document.Clear();
        public void SetPaused(bool paused){Paused=paused;Document.Cancel();}
        public void Dispose(){Disposed=true;Closed?.Invoke(this,EventArgs.Empty);}
    }
}
