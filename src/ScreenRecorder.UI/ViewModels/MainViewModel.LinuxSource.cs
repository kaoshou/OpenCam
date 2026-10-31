// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.ViewModels;

public partial class MainViewModel
{
    private CancellationTokenSource? linuxSourceCancellation;
    private bool isSelectingLinuxSource;
    public bool UsesLinuxPortal => OperatingSystem.IsLinux() && ScreenRecorder.Media.Capture.ObsRuntime.DefaultBackend("linux-x64")=="pipewire";
    public LinuxPortalSelectionView? LinuxPortalSelection {get;private set;}
    public bool IsSelectingLinuxSource {
        get=>isSelectingLinuxSource;
        private set {
            if(!SetProperty(ref isSelectingLinuxSource,value))return;
            foreach(var name in new[]{nameof(CanStartRecording),nameof(CanEditRecordingSettings),nameof(CanSelectMonitor),nameof(CanConfigureCustomRegion)})OnPropertyChanged(name);
        }
    }
    [RelayCommand]
    private void CancelLinuxSourceSelection()=>linuxSourceCancellation?.Cancel();

    [RelayCommand]
    public async Task SelectLinuxSourceAsync()
    {
        if(!UsesLinuxPortal||!CanEditRecordingSettings||IsProjectWorkspaceOpen)return;
        IsSelectingLinuxSource=true;
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(130));
        linuxSourceCancellation=cancellation;
        var operation=Guid.NewGuid();Guid? server=null;
        var finished=false;
        try {
            StatusMessage=Strings["LinuxPortalSelecting"];
            await EnsureRecorderProcessAsync();
            var response=await _ipcClient.SendCommandAsync("BeginLinuxSourceSelection",new LinuxSourceSelectionRequest(operation),cancellationToken:cancellation.Token);
            var status=ReadSelection(response,operation,null);server=status.ServerInstanceId;
            while(status.Phase=="selecting") {
                await Task.Delay(200,cancellation.Token);
                response=await _ipcClient.SendCommandAsync("GetLinuxSourceSelection",new LinuxSourceSelectionRequest(operation,server),cancellationToken:cancellation.Token);
                status=ReadSelection(response,operation,server);
            }
            finished=true;
            if(status.Phase=="ready"&&status.Selection is {} selection) {
                ApplyLinuxPortalSelection(selection);
                StatusMessage=Strings["LinuxPortalSelected"];
            }else StatusMessage=Strings[status.Phase=="cancelled"?"LinuxPortalCancelled":"LinuxPortalFailed"];
        } catch(OperationCanceledException) {StatusMessage=Strings["LinuxPortalCancelled"];}
        catch(Exception error) {
            Serilog.Log.Warning("Linux selection UI failed: ErrorType={ErrorType}",error.GetType().Name);
            StatusMessage=Strings["LinuxPortalFailed"];
        } finally {
            if(!finished&&server is {} instance)
                await _ipcClient.SendCommandAsync("CancelLinuxSourceSelection",new LinuxSourceSelectionRequest(operation,instance),timeoutMs:3000);
            linuxSourceCancellation=null;IsSelectingLinuxSource=false;
        }
    }
    private static LinuxSourceSelectionSnapshot ReadSelection(ScreenRecorder.Infrastructure.IPC.IpcResponse response,Guid operation,Guid? server)
    {
        if(!response.Success||response.DataJson is null)throw new IOException("Source selection unavailable.");
        var value=JsonSerializer.Deserialize<LinuxSourceSelectionSnapshot>(response.DataJson)??throw new IOException("Source selection unavailable.");
        if(value.OperationId!=operation||value.ServerInstanceId==Guid.Empty||server is {} expected&&expected!=value.ServerInstanceId||
            value.Phase is not("selecting" or "ready" or "cancelled" or "failed"))throw new IOException("Source selection identity changed.");
        return value;
    }
    internal void ApplyLinuxPortalSelection(LinuxPortalSelectionView view)
    {
        if(_displayService is not LinuxDesktopCatalog catalog)throw new IOException("Linux display service is unavailable.");
        catalog.UseAuthorizedSourceView(view);
        _updatingCapturePreferences=true;
        try {
            LinuxPortalSelection=view;
            AvailableMonitors.Clear();
            var monitor=new MonitorDisplayOption(0,Strings["LinuxPortalSource"],view.Width,view.Height,false);
            AvailableMonitors.Add(monitor);SelectedMonitor=monitor;
            RegionX=0;RegionY=0;RegionWidth=view.Width&~1;RegionHeight=view.Height&~1;
            IsCustomRegion=false;IsMonitorSelected=true;
        }finally{_updatingCapturePreferences=false;}
        OnPropertyChanged(nameof(LinuxPortalSelection));OnPropertyChanged(nameof(CanStartRecording));
        PersistUserSettings();
    }
    private void InvalidateLinuxPortalSource()
    {
        if(!UsesLinuxPortal)return;
        LinuxPortalSelection=null;
        if(_displayService is LinuxDesktopCatalog catalog)catalog.UseAuthorizedSourceView(null);
        AvailableMonitors.Clear();SelectedMonitor=null;
        OnPropertyChanged(nameof(LinuxPortalSelection));OnPropertyChanged(nameof(CanStartRecording));
    }
}
