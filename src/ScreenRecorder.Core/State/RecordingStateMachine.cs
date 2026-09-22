// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;

namespace ScreenRecorder.Core.State;

public class StateChangedEventArgs : EventArgs
{
    public RecordingState PreviousState { get; }
    public RecordingState NewState { get; }
    public string? Reason { get; }

    public StateChangedEventArgs(RecordingState previousState, RecordingState newState, string? reason = null)
    {
        PreviousState = previousState;
        NewState = newState;
        Reason = reason;
    }
}

/// <summary>
/// 執行緒安全之錄影狀態機 (防止 Race Condition 與非法狀態轉移)
/// </summary>
public interface IRecordingStateMachine
{
    RecordingState CurrentState { get; }
    bool CanTransitionTo(RecordingState targetState);
    bool TryTransition(RecordingState targetState, string? reason = null);
    void ForceTransition(RecordingState targetState, string? reason = null);
    void Reset();
    event EventHandler<StateChangedEventArgs>? StateChanged;
}

public class RecordingStateMachine : IRecordingStateMachine
{
    private readonly object _syncLock = new();
    private RecordingState _currentState = RecordingState.Idle;

    public event EventHandler<StateChangedEventArgs>? StateChanged;

    public RecordingState CurrentState
    {
        get
        {
            lock (_syncLock)
            {
                return _currentState;
            }
        }
    }

    public bool CanTransitionTo(RecordingState targetState)
    {
        lock (_syncLock)
        {
            return IsValidTransition(_currentState, targetState);
        }
    }

    public bool TryTransition(RecordingState targetState, string? reason = null)
    {
        RecordingState oldState;
        lock (_syncLock)
        {
            if (!IsValidTransition(_currentState, targetState))
            {
                return false;
            }

            oldState = _currentState;
            _currentState = targetState;
        }

        StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, targetState, reason));
        return true;
    }

    public void ForceTransition(RecordingState targetState, string? reason = null)
    {
        RecordingState oldState;
        lock (_syncLock)
        {
            oldState = _currentState;
            _currentState = targetState;
        }

        StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, targetState, reason));
    }

    public void Reset()
    {
        ForceTransition(RecordingState.Idle, "重設狀態機至 Idle");
    }

    private static bool IsValidTransition(RecordingState from, RecordingState to)
    {
        // 相同狀態不轉移
        if (from == to) return false;

        // 任何活躍狀態均可因異常切換至 Interrupted 或 Failed
        if (to == RecordingState.Interrupted || to == RecordingState.Failed)
        {
            return from != RecordingState.Idle;
        }

        return from switch
        {
            RecordingState.Idle => to == RecordingState.Preparing || to == RecordingState.Recoverable,
            RecordingState.Preparing => to == RecordingState.Recording || to == RecordingState.Failed || to == RecordingState.Idle,
            RecordingState.Recording => to == RecordingState.Pausing || to == RecordingState.Stopping,
            RecordingState.Pausing => to == RecordingState.Paused || to == RecordingState.Stopping,
            RecordingState.Paused => to == RecordingState.Recording || to == RecordingState.Stopping,
            RecordingState.Stopping => to == RecordingState.Finalizing,
            RecordingState.Finalizing => to == RecordingState.Completed,
            RecordingState.Completed => to == RecordingState.Idle || to == RecordingState.Preparing,
            RecordingState.Interrupted => to == RecordingState.Recoverable || to == RecordingState.Failed || to == RecordingState.Idle,
            RecordingState.Recoverable => to == RecordingState.Finalizing || to == RecordingState.Completed || to == RecordingState.Failed || to == RecordingState.Idle,
            RecordingState.Failed => to == RecordingState.Idle || to == RecordingState.Preparing,
            _ => false
        };
    }
}
