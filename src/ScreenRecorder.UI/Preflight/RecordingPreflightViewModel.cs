// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.UI.Preflight;

public sealed class RecordingPreflightViewModel(Guid operation) : ObservableObject
{
    public static string AudioStatusKey(PreflightPhase phase, PreflightSourceState state) => state is PreflightSourceState.Disabled or PreflightSourceState.Unavailable
        ? "PreflightSource" + state : phase switch
        {
            PreflightPhase.TrialStarting => "PreflightTrialStarting",
            PreflightPhase.TrialFinalizing => "PreflightTrialFinalizing",
            PreflightPhase.TrialRecording when state == PreflightSourceState.Waiting => "PreflightAudioTrialWaiting",
            PreflightPhase.Reviewing => "PreflightAudioReview",
            _ => "PreflightSource" + state
        };
    public PreflightStatus Status { get; private set; } = PreflightStatus.Empty with { OperationId = operation, Phase = PreflightPhase.Preparing };
    private Guid? _server;
    private long _sequence;
    private bool _busy, _connectionFailed;
    public bool ConnectionFailed { get => _connectionFailed; set { SetProperty(ref _connectionFailed, value); Changed(); } }
    public bool Busy { get => _busy; set { SetProperty(ref _busy, value); Changed(); } }
    public bool ShowSilenceNotice => Status.Phase == PreflightPhase.Checking && (Status.Microphone.State == PreflightSourceState.Silent ||
        Status.SystemAudio.State == PreflightSourceState.Silent ||
        Status.Microphone.State == PreflightSourceState.Disabled && Status.SystemAudio.State == PreflightSourceState.Disabled);
    private static bool Readable(PreflightSourceResult source) => source.State is
        PreflightSourceState.Disabled or PreflightSourceState.Live or PreflightSourceState.Silent;
    public bool CanStart => !Busy && !ConnectionFailed && Status.Phase == PreflightPhase.Checking && Status.HasFrame && Status.ErrorCode is null &&
        Readable(Status.Microphone) && Readable(Status.SystemAudio);
    public bool CanTrial => !Busy && !ConnectionFailed && Status.ErrorCode is null && !Status.Playing &&
        (Status.Phase == PreflightPhase.Checking && Status.HasFrame || Status.Phase == PreflightPhase.Reviewing && Status.HasTrial);
    public bool CanReplay => !Busy && Status.Phase == PreflightPhase.Reviewing && Status.HasTrial && !Status.Playing && Status.ErrorCode is null;
    public bool Apply(PreflightReply reply)
    {
        if (reply.Sequence < _sequence || reply.Status.OperationId != operation || _server is { } server && server != reply.ServerInstanceId ||
            reply.Status.ConfigurationRevision < Status.ConfigurationRevision && reply.Status.Phase != PreflightPhase.Closed) return false;
        _server ??= reply.ServerInstanceId;
        _sequence = reply.Sequence;
        ConnectionFailed = false;
        Status = reply.Status;
        OnPropertyChanged(nameof(Status)); Changed(); return true;
    }
    private void Changed()
    {
        OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(CanTrial)); OnPropertyChanged(nameof(CanReplay));
        OnPropertyChanged(nameof(ShowSilenceNotice));
    }
}
