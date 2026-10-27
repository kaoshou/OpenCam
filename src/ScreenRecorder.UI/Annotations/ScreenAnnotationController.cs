// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Annotations;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.Annotations;

public interface IAnnotationHost:IDisposable
{
    AnnotationDocument Document {get;}
    event EventHandler? Closed;
    void Show();
    void SetPaused(bool paused);
}

/// <summary>Optional UI-only lifetime. Never sends recorder commands or creates a process.</summary>
public sealed class ScreenAnnotationController(Func<CaptureRegion,IAnnotationHost> createHost):IDisposable
{
    private IAnnotationHost? _host;
    private CaptureRegion _region=CaptureRegion.Empty;
    private bool _enabled,_recording,_paused,_disposed;
    public bool IsOpen=>_host is not null;
    public event EventHandler<Exception>? Failed;
    public void Update(bool enabled,bool recording,bool paused,CaptureRegion region)
    {
        if(_disposed)return;
        if(_region!=region||!enabled||(!recording&&!paused))Close();
        _enabled=enabled;_recording=recording;_paused=paused;_region=region;
        if(_host is not null)Guard(()=>{_host.Document.Cancel();_host.SetPaused(paused||!recording);});
    }
    public void Open()
    {
        if(_disposed||!_enabled||!_recording||_paused||_host is not null||!_region.IsValid||
            _region.Width>32768||_region.Height>32768)return;
        Guard(()=>{_host=createHost(_region);_host.Closed+=OnClosed;_host.Show();});
    }
    public void Close()
    {
        var host=_host;_host=null;if(host is null)return;
        host.Closed-=OnClosed;
        try{host.Document.Cancel();host.Dispose();}
        catch(Exception e){Report(e);}
    }
    private void OnClosed(object? sender,EventArgs e)=>Close();
    private void Guard(Action action){try{action();}catch(Exception e){Close();Report(e);}}
    private void Report(Exception e)
    {
        Serilog.Log.Warning(e,"Optional screen annotations unavailable; recording is unchanged");
        try{Failed?.Invoke(this,e);}catch(Exception subscriber){Serilog.Log.Warning(subscriber,"Annotation warning subscriber failed");}
    }
    public void Dispose(){if(_disposed)return;_disposed=true;Close();Failed=null;}
}
