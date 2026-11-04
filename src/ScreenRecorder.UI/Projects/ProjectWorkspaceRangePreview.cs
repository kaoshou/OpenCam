// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Infrastructure.IPC;
namespace ScreenRecorder.UI.Projects;
public sealed partial class ProjectWorkspaceViewModel
{
    private sealed record RangePlayback(Guid? Project,Guid? Server,long Revision,long Start,long End);
    private RangePlayback? _rangePlayback;
    [ObservableProperty] private bool _repeatPreviewRange;
    private ProjectPreviewAudioLevel? _previewLevel;
    public bool CanPreviewRange => CanChangePreviewOptions && CanDeleteRange;
    public double PreviewMixPeak => (_previewLevel?.Peak??0)*100;
    public string PreviewMixText => !IsPlayingPreview ? Strings["EditorMeterIdle"] : _previewLevel is not { } level ? Strings["EditorMeterNoData"] :
        Strings[PreviewMuted?"EditorMeterMuted":"EditorMeterTitle"] + $" · RMS {20*Math.Log10(Math.Max(1e-6,level.Rms)):0.0} dBFS" +
            (level.Overload?" · "+Strings["EditorMeterOverload"]:"");
    [RelayCommand]
    public async Task PreviewRangeAsync()
    {
        if(!CanPreviewRange)return;
        var range=new RangePlayback(State.ProjectId,State.ServerInstanceId,State.Revision,RangeStartTicks!.Value,RangeEndTicks!.Value);
        var stop=StopPreviewAsync();
        var intent=_playbackIntent;
        if(!await stop || intent!=_playbackIntent || !RangeIsCurrent(range))return;
        _rangePlayback=range;
        await StartPreviewAsync(range.Start,++_playbackIntent);
    }
    private bool RangeIsCurrent(RangePlayback range) => !StatusUnconfirmed && !IsExporting &&
        State.Mode is ScreenRecorder.Core.Projects.ProjectMode.Ready or ScreenRecorder.Core.Projects.ProjectMode.Paused && State.ProjectId==range.Project &&
        State.ServerInstanceId==range.Server && State.Revision==range.Revision && RangeStartTicks==range.Start && RangeEndTicks==range.End;
    private void CheckRangePlayback()
    {
        OnPropertyChanged(nameof(CanPreviewRange));
        if(_rangePlayback is { } range && !RangeIsCurrent(range)) _=StopPreviewAsync();
    }
    private async Task RepeatRangeIfNeededAsync(long intent,ProjectPlaybackState playback)
    {
        if(!RepeatPreviewRange || playback.Error is not null || _rangePlayback is not { } range ||
            !RangeIsCurrent(range) || playback.TimelineTicks!=range.End || intent!=_playbackIntent)return;
        await _playbackGate.WaitAsync();
        try
        {
            if(intent!=_playbackIntent || !RepeatPreviewRange || !RangeIsCurrent(range))return;
            if(!await StopPreviewCoreAsync() || intent!=_playbackIntent || !RepeatPreviewRange || !RangeIsCurrent(range))return;
            await StartPreviewCoreAsync(range.Start,intent);
        }
        finally{_playbackGate.Release();}
    }
    private void UpdatePreviewLevel(ProjectPreviewAudioLevel? level)
    {
        _previewLevel=level is { } value && double.IsFinite(value.Peak) && double.IsFinite(value.Rms) &&
            value.Peak is >=0 and <=1 && value.Rms is >=0 and <=1 ? level:null;
        OnPropertyChanged(nameof(PreviewMixPeak));OnPropertyChanged(nameof(PreviewMixText));
    }
}
