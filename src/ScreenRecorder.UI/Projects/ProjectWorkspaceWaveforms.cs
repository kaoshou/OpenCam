// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    private readonly Dictionary<Guid, ProjectWaveformResult> _waveforms = new();
    private readonly HashSet<Guid> _waveformFailures = new();
    private int _pollingWaveform;
    public IReadOnlyDictionary<Guid, ProjectWaveformResult> Waveforms => _waveforms;
    public string WaveformStatus(Guid clipId) => Strings[_waveformFailures.Contains(clipId)
        ? "ProjectWaveformUnavailable" : "ProjectWaveformLoading"];

    private void FailWaveform(Guid clipId)
    {
        _waveformFailures.Add(clipId);
        OnPropertyChanged(nameof(Waveforms));
    }

    private void ClearWaveforms()
    {
        _waveforms.Clear();
        _waveformFailures.Clear();
        OnPropertyChanged(nameof(Waveforms));
    }

    public async Task PollWaveformAsync()
    {
        if (!CanEdit || State.ProjectId is null || _waveforms.Count >= 256 ||
            Interlocked.CompareExchange(ref _pollingWaveform, 1, 0) != 0) return;
        try
        {
            var snapshot = State;
            bool Missing(ProjectClip c) => !_waveforms.ContainsKey(c.Id) && !_waveformFailures.Contains(c.Id);
            var clip = SelectedClip is { } selected && Missing(selected) ? selected : Clips.FirstOrDefault(Missing);
            if (clip is null) return;
            var reply = await client.SendAsync("GetProjectWaveform", new() {
                ProjectId = snapshot.ProjectId, ExpectedRevision = snapshot.Revision, ClipId = clip.Id });
            if (State.ProjectId != snapshot.ProjectId || State.Revision != snapshot.Revision ||
                State.ServerInstanceId != snapshot.ServerInstanceId) return;
            if (!reply.Success || reply.Unconfirmed) { FailWaveform(clip.Id); return; }
            if (reply.State.ProjectId != snapshot.ProjectId || reply.State.Revision != snapshot.Revision ||
                reply.State.ServerInstanceId != snapshot.ServerInstanceId ||
                reply.Waveform is not { } wave || wave.ClipId != clip.Id || wave.Revision != snapshot.Revision) return;
            if (wave.Error is not null) { FailWaveform(clip.Id); return; }
            if (wave.Data is not { } data) return; // Background work is pending; never mark the project unsaved.
            if (data.SampleCount <= 0 || data.Buckets.IsDefaultOrEmpty || data.Buckets.Length > 256 ||
                data.Buckets.Any(b => !float.IsFinite(b.Minimum) || !float.IsFinite(b.Maximum) ||
                    !float.IsFinite(b.Rms) || b.Minimum > b.Maximum || b.Rms < 0))
            { FailWaveform(clip.Id); return; }
            _waveforms[clip.Id] = data;
            OnPropertyChanged(nameof(Waveforms));
        }
        catch { /* Rebuildable media must not turn a saved edit into an uncertain transaction. */ }
        finally { Interlocked.Exchange(ref _pollingWaveform, 0); }
    }
}
