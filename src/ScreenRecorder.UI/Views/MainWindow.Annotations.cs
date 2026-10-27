// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Interactivity;
using Avalonia.Threading;
using ScreenRecorder.UI.Annotations;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.Core.Annotations;

namespace ScreenRecorder.UI.Views;

public partial class MainWindow
{
    private ScreenAnnotationController? _annotations;
    private bool _annotationUpdateQueued;
    private void OnScreenAnnotationsClicked(object? sender,RoutedEventArgs e)
        =>OpenScreenAnnotations();
    private void OpenScreenAnnotations()
    {
        if(DataContext is not MainViewModel {CanOpenScreenAnnotations:true} vm)return;
        try
        {
            if(_annotations is null)
            {
                _annotations=new(region=>new AnnotationOverlayHost(this,region,vm.ScreenAnnotationColor,vm.ScreenAnnotationWidth,vm.SaveScreenAnnotationStyle,()=>vm.IsAnnotationHotkeyRegistered,finishShortcut:vm.MatchesAnnotationFinish));
                _annotations.Failed+=(_,_)=>vm.ScreenAnnotationNotice=vm.Strings["AnnotationsUnavailable"];
            }
            vm.ScreenAnnotationNotice=null;
            UpdateAnnotations();_annotations.Open();
        }
        catch(Exception error){Serilog.Log.Warning(error,"Optional screen annotation entry failed; recording unchanged");CloseAnnotations();}
    }
    private void RunAnnotationCommand(string command)
    {
        if(DataContext is not MainViewModel {CanOpenScreenAnnotations:true} vm)return;
        if(_annotations is null)
        {
            _annotations=new(region=>new AnnotationOverlayHost(this,region,vm.ScreenAnnotationColor,vm.ScreenAnnotationWidth,vm.SaveScreenAnnotationStyle,()=>vm.IsAnnotationHotkeyRegistered,finishShortcut:vm.MatchesAnnotationFinish));
            _annotations.Failed+=(_,_)=>vm.ScreenAnnotationNotice=vm.Strings["AnnotationsUnavailable"];
        }
        UpdateAnnotations();
        switch(command)
        {
            case "togglePen":_annotations.TogglePen();break;
            case "pen":_annotations.SelectTool(AnnotationTool.Pen);break;
            case "line":_annotations.SelectTool(AnnotationTool.Line);break;
            case "rectangle":_annotations.SelectTool(AnnotationTool.Rectangle);break;
            case "ellipse":_annotations.SelectTool(AnnotationTool.Ellipse);break;
        }
    }
    private void QueueAnnotationsUpdate()
    {
        if(_annotationUpdateQueued)return;_annotationUpdateQueued=true;
        Dispatcher.UIThread.Post(()=>{_annotationUpdateQueued=false;UpdateAnnotations();},DispatcherPriority.Background);
    }
    private void UpdateAnnotations()
    {
        if(DataContext is not MainViewModel vm){CloseAnnotations();return;}
        vm.NotifyAnnotationAvailability();
        if(_annotations is null)return;
        try{_annotations.Update(vm.ScreenAnnotationsEnabled,vm.IsRecording&&!vm.IsPreparing,
            vm.IsPaused||vm.IsPreparing,vm.ScreenAnnotationRegion);}
        catch(Exception error){Serilog.Log.Warning(error,"Annotation lifecycle could not be updated");CloseAnnotations();}
    }
    private void CloseAnnotations(){var controller=_annotations;_annotations=null;controller?.Dispose();}
}
