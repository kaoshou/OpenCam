// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.State;
using Xunit;

namespace ScreenRecorder.Core.Tests;

public class RecordingStateMachineTests
{
    [Fact]
    public void StateMachine_InitialState_ShouldBeIdle()
    {
        var sm = new RecordingStateMachine();
        Assert.Equal(RecordingState.Idle, sm.CurrentState);
    }

    [Fact]
    public void StateMachine_ValidLifecycleTransition_ShouldSucceed()
    {
        var sm = new RecordingStateMachine();

        // Idle -> Preparing
        Assert.True(sm.TryTransition(RecordingState.Preparing));
        Assert.Equal(RecordingState.Preparing, sm.CurrentState);

        // Preparing -> Recording
        Assert.True(sm.TryTransition(RecordingState.Recording));
        Assert.Equal(RecordingState.Recording, sm.CurrentState);

        // Recording -> Pausing -> Paused -> Recording
        Assert.True(sm.TryTransition(RecordingState.Pausing));
        Assert.True(sm.TryTransition(RecordingState.Paused));
        Assert.True(sm.TryTransition(RecordingState.Recording));

        // Recording -> Stopping -> Finalizing -> Completed
        Assert.True(sm.TryTransition(RecordingState.Stopping));
        Assert.True(sm.TryTransition(RecordingState.Finalizing));
        Assert.True(sm.TryTransition(RecordingState.Completed));

        // Completed -> Idle
        Assert.True(sm.TryTransition(RecordingState.Idle));
        Assert.Equal(RecordingState.Idle, sm.CurrentState);
    }

    [Fact]
    public void StateMachine_InvalidTransition_ShouldBeRejected()
    {
        var sm = new RecordingStateMachine();

        // 處於 Idle 時不可以直接切換到 Recording 或 Stopping
        Assert.False(sm.TryTransition(RecordingState.Recording));
        Assert.Equal(RecordingState.Idle, sm.CurrentState);

        Assert.False(sm.TryTransition(RecordingState.Stopping));
        Assert.Equal(RecordingState.Idle, sm.CurrentState);

        Assert.False(sm.TryTransition(RecordingState.Finalizing));
        Assert.Equal(RecordingState.Idle, sm.CurrentState);
    }

    [Fact]
    public void StateMachine_ActiveState_CanTransitionToInterruptedOrFailed()
    {
        var sm = new RecordingStateMachine();
        sm.TryTransition(RecordingState.Preparing);
        sm.TryTransition(RecordingState.Recording);

        // 錄影中發生中斷
        Assert.True(sm.TryTransition(RecordingState.Interrupted, "模擬外力中斷"));
        Assert.Equal(RecordingState.Interrupted, sm.CurrentState);

        // 中斷後標記為可救援
        Assert.True(sm.TryTransition(RecordingState.Recoverable));
        Assert.Equal(RecordingState.Recoverable, sm.CurrentState);
    }

    [Fact]
    public void StateMachine_StateChangedEvent_ShouldFireWithCorrectArgs()
    {
        var sm = new RecordingStateMachine();
        StateChangedEventArgs? receivedArgs = null;

        sm.StateChanged += (s, e) => receivedArgs = e;

        sm.TryTransition(RecordingState.Preparing, "User clicked start");

        Assert.NotNull(receivedArgs);
        Assert.Equal(RecordingState.Idle, receivedArgs!.PreviousState);
        Assert.Equal(RecordingState.Preparing, receivedArgs.NewState);
        Assert.Equal("User clicked start", receivedArgs.Reason);
    }

    [Fact]
    public void StateMachine_PauseAndStopDirectly_ShouldSucceed()
    {
        var sm = new RecordingStateMachine();
        sm.TryTransition(RecordingState.Preparing);
        sm.TryTransition(RecordingState.Recording);

        // 暫停
        Assert.True(sm.TryTransition(RecordingState.Pausing));
        Assert.True(sm.TryTransition(RecordingState.Paused));
        Assert.Equal(RecordingState.Paused, sm.CurrentState);

        // 在暫停狀態下使用者直接停止錄影
        Assert.True(sm.TryTransition(RecordingState.Stopping));
        Assert.Equal(RecordingState.Stopping, sm.CurrentState);

        Assert.True(sm.TryTransition(RecordingState.Finalizing));
        Assert.True(sm.TryTransition(RecordingState.Completed));
        Assert.Equal(RecordingState.Completed, sm.CurrentState);
    }

    [Fact]
    public void StateMachine_Paused_CanTransitionToInterruptedOrRecoverable()
    {
        var sm = new RecordingStateMachine();
        sm.TryTransition(RecordingState.Preparing);
        sm.TryTransition(RecordingState.Recording);
        sm.TryTransition(RecordingState.Pausing);
        sm.TryTransition(RecordingState.Paused);

        // 暫停中遭遇外力中斷
        Assert.True(sm.TryTransition(RecordingState.Interrupted, "暫停時進程異常中斷"));
        Assert.Equal(RecordingState.Interrupted, sm.CurrentState);

        // 轉為可救援
        Assert.True(sm.TryTransition(RecordingState.Recoverable));
        Assert.Equal(RecordingState.Recoverable, sm.CurrentState);
    }
}
