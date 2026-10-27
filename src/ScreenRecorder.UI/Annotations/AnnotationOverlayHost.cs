// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ScreenRecorder.Core.Annotations;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.Annotations;

internal sealed class AnnotationOverlayHost:IAnnotationHost
{
    private readonly Window _overlay;
    private readonly AnnotationCanvas _canvas;
    private readonly AnnotationToolbar _toolbar;
    private readonly PixelPoint _origin;
    private readonly double _scale;
    private readonly Func<bool> _shortcutAvailable;
    private readonly Func<Window,bool,bool> _setInteractive;
    private bool _closed,_paused,_shown,_toolbarHidden;
    private static PixelPoint? _lastToolbarPosition;
    public AnnotationDocument Document{get;}
    public event EventHandler? Closed;
    public AnnotationOverlayHost(Window owner,CaptureRegion region,uint color,double width,Action<uint,double> saveStyle,Func<bool>? shortcutAvailable=null,Func<Window,bool,bool>? setInteractive=null)
    {
        var screen=owner.Screens.ScreenFromPoint(new(region.X,region.Y))??throw new InvalidOperationException("Annotation display unavailable");
        var bounds=screen.Bounds;
        if(region.X<bounds.X||region.Y<bounds.Y||(long)region.X+region.Width>bounds.Right||(long)region.Y+region.Height>bounds.Bottom)
            throw new InvalidOperationException("Annotation region spans displays");
        _scale=screen.Scaling;_origin=new(region.X,region.Y);_shortcutAvailable=shortcutAvailable??(()=>false);_setInteractive=setInteractive??AnnotationNativeWindow.SetInteractive;
        Document=new(region.Width/_scale,region.Height/_scale){Color=color,StrokeWidth=width};
        _canvas=new(Document);
        _overlay=new Window{Title="OpenCam Annotations",SystemDecorations=SystemDecorations.None,ShowInTaskbar=false,
            CanResize=false,Topmost=true,ShowActivated=false,Background=Brushes.Transparent,
            TransparencyLevelHint=[WindowTransparencyLevel.Transparent],Content=_canvas,Width=region.Width/_scale,Height=region.Height/_scale,
            Position=_origin,WindowStartupLocation=WindowStartupLocation.Manual};
        _toolbar=new(Document,SetMode,Dispose,saveStyle,Fail,()=>{SetMode(false);owner.WindowState=WindowState.Minimized;},ToggleToolbar,_shortcutAvailable){WindowStartupLocation=WindowStartupLocation.Manual};
        var x=_lastToolbarPosition?.X??region.X+16;var y=_lastToolbarPosition?.Y??region.Y+16;
        _toolbar.Position=new(Math.Clamp(x,bounds.X,Math.Max(bounds.X,bounds.Right-(int)Math.Ceiling(_toolbar.Width*_scale))),
            Math.Clamp(y,bounds.Y,Math.Max(bounds.Y,bounds.Bottom-(int)Math.Ceiling(190*_scale))));
        _toolbar.Closed+=(_,_)=>Dispose();_overlay.Closed+=(_,_)=>Dispose();
        _overlay.KeyDown+=(_,e)=>{if(e.Key==Key.Escape){Guard(()=>SetMode(false));e.Handled=true;}};
        _overlay.Activated+=(_,_)=>AnnotationNativeWindow.RaiseExistingPointer();
        _canvas.PointerPressed+=(_,_)=>AnnotationNativeWindow.RaiseExistingPointer();
    }
    public void Show()
    {
        if(_closed)return;
        _overlay.Show();_overlay.Position=_origin;
        // Create input policy before yielding to the event loop. Failure closes both windows.
        if(!_setInteractive(_overlay,false))throw new InvalidOperationException("Native annotation input policy unavailable");
        // No marks need compositing yet. Keeping the full-screen surface hidden
        // leaves the main window and other applications directly clickable.
        _overlay.Hide();
        _toolbar.Show();AnnotationNativeWindow.ConfigureToolbar(_toolbar);
        _shown=true;AnnotationNativeWindow.RaiseExistingPointer();
    }
    private void SetMode(bool drawing)
    {
        if(_closed||_paused)return;
        _canvas.CancelGesture();
        if(drawing&&!_overlay.IsVisible){_overlay.Show();_overlay.Position=_origin;}
        if(!_setInteractive(_overlay,drawing))throw new InvalidOperationException("Cannot switch native annotation input policy");
        _canvas.Interactive=drawing;_toolbar.SetMode(drawing);
        if(drawing){_overlay.Activate();_canvas.Focus();}
        else if(Document.Marks.IsEmpty)_overlay.Hide();
        AnnotationNativeWindow.ConfigureToolbar(_toolbar);
        AnnotationNativeWindow.RaiseExistingPointer();
    }
    public void SetPaused(bool paused)
    {
        if(_closed||_paused==paused)return;
        _canvas.Interactive=false;Document.Cancel();
        if(!_setInteractive(_overlay,false))throw new InvalidOperationException("Cannot release annotation input");
        _toolbar.SetMode(false);_paused=paused;
        if(paused){_toolbar.Hide();_overlay.Hide();}
        else{if(!Document.Marks.IsEmpty){_overlay.Show();_overlay.Position=_origin;}if(_toolbarHidden&&!_shortcutAvailable())_toolbarHidden=false;if(!_toolbarHidden){_toolbar.Show();AnnotationNativeWindow.ConfigureToolbar(_toolbar);}}
    }
    public void ToggleToolbar()
    {
        if(_closed||_paused)return;
        if(_toolbar.IsVisible){if(!_shortcutAvailable())return;SetMode(false);_toolbar.Hide();_toolbarHidden=true;}
        else{_toolbar.Show();_toolbarHidden=false;AnnotationNativeWindow.ConfigureToolbar(_toolbar);AnnotationNativeWindow.RaiseExistingPointer();}
    }
    private void Guard(Action action){try{action();}catch(Exception e){Fail(e);}}
    private void Fail(Exception e){Serilog.Log.Warning(e,"Optional annotation window closed; recording is unchanged");Dispose();}
    public void Dispose()
    {
        if(_closed)return;_closed=true;
        if(_shown)_lastToolbarPosition=_toolbar.Position;
        _canvas.Dispose();
        try{_toolbar.Dispose();}finally{try{_overlay.Close();}finally{Closed?.Invoke(this,EventArgs.Empty);Closed=null;}}
    }
}
