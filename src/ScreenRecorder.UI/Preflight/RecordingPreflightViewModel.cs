// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.UI.Preflight;

public sealed class RecordingPreflightViewModel(Guid operation) : ObservableObject
{
    public PreflightStatus Status { get; private set; } = PreflightStatus.Empty;
    private Guid? _server;
    private bool _screen, _silence, _busy, _connectionFailed;
    public bool ConnectionFailed { get => _connectionFailed; set { SetProperty(ref _connectionFailed, value); Changed(); } }
    public bool ScreenConfirmed { get => _screen; set { SetProperty(ref _screen, value); Changed(); } }
    public bool SilenceAcknowledged { get => _silence; set { SetProperty(ref _silence, value); Changed(); } }
    public bool Busy { get => _busy; set { SetProperty(ref _busy, value); Changed(); } }
    public bool NeedsSilenceAcknowledgement => Status.Microphone.State == PreflightSourceState.Silent ||
        Status.SystemAudio.State == PreflightSourceState.Silent ||
        Status.Microphone.State == PreflightSourceState.Disabled && Status.SystemAudio.State == PreflightSourceState.Disabled;
    private static bool Readable(PreflightSourceResult source) => source.State is
        PreflightSourceState.Disabled or PreflightSourceState.Live or PreflightSourceState.Silent;
    public bool CanStart => !Busy && !ConnectionFailed && Status.Phase == PreflightPhase.Checking && Status.HasFrame && Status.ErrorCode is null &&
        Readable(Status.Microphone) && Readable(Status.SystemAudio) && ScreenConfirmed && (!NeedsSilenceAcknowledgement || SilenceAcknowledged);
    public bool CanTrial => !Busy && !ConnectionFailed && Status.ErrorCode is null && !Status.Playing &&
        (Status.Phase == PreflightPhase.Checking && Status.HasFrame || Status.Phase == PreflightPhase.Reviewing && Status.HasTrial);
    public bool CanReplay => !Busy && Status.Phase == PreflightPhase.Reviewing && Status.HasTrial && !Status.Playing && Status.ErrorCode is null;
    public bool Apply(PreflightReply reply)
    {
        if (reply.Status.OperationId != operation || _server is { } server && server != reply.ServerInstanceId ||
            reply.Status.ConfigurationRevision < Status.ConfigurationRevision && reply.Status.Phase != PreflightPhase.Closed) return false;
        _server ??= reply.ServerInstanceId;
        ConnectionFailed = false;
        Status = reply.Status;
        if (Status.ErrorCode is not null || Status.Phase != PreflightPhase.Checking || !Status.HasFrame)
        { ScreenConfirmed = false; SilenceAcknowledged = false; }
        OnPropertyChanged(nameof(Status)); Changed(); return true;
    }
    private void Changed()
    {
        OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(CanTrial)); OnPropertyChanged(nameof(CanReplay));
        OnPropertyChanged(nameof(NeedsSilenceAcknowledgement));
    }
}
