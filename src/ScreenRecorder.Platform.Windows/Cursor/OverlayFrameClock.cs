// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Serilog;

namespace ScreenRecorder.Platform.Windows.Cursor;

internal sealed record OverlayFrameStatistics(long Samples, long MovedUpdates, double ElapsedSeconds,
    double? MeanIntervalMs, double? MaxIntervalMs, long MissedDeadlines);

/// <summary>Only schedules decoration. Never controls capture timestamps or audio.</summary>
internal sealed class OverlayFrameClock
{
    private readonly long _frequency, _period, _started;
    private readonly Func<long> _timestamp;
    private readonly Action<TimeSpan> _wait;
    private long _next, _last, _samples, _moved, _intervals, _maxInterval, _missed;

    public OverlayFrameClock(long frequency, Func<long> timestamp, Action<TimeSpan> wait)
    {
        if (frequency < 60) throw new ArgumentOutOfRangeException(nameof(frequency));
        _frequency=frequency;_timestamp=timestamp;_wait=wait;
        _period=(frequency+30)/60;
        _started=timestamp();_next=_started+_period;
    }

    public void WaitNext()
    {
        var now=_timestamp();
        if (now<_next)
            _wait(TimeSpan.FromTicks(Math.Max(1,(long)Math.Ceiling(
                (_next-now)*(double)TimeSpan.TicksPerSecond/_frequency))));
        now=_timestamp();
        var skipped=now>_next?(now-_next)/_period:0;
        _missed+=skipped;
        _next+=(skipped+1)*_period;
    }

    public void NoteUpdate(bool moved=false)
    {
        var now=_timestamp();
        if (_samples>0)
        {
            var interval=Math.Max(0,now-_last);
            _intervals+=interval;_maxInterval=Math.Max(_maxInterval,interval);
        }
        _last=now;_samples++;
        if(moved)_moved++;
    }

    public OverlayFrameStatistics Snapshot()=>new(_samples,_moved,
        Math.Max(0,_timestamp()-_started)/(double)_frequency,
        _samples>1?1000d*_intervals/_frequency/(_samples-1):null,
        _samples>1?1000d*_maxInterval/_frequency:null,_missed);
}

/// <summary>A bounded scoped wait; no global timer-resolution changes or spinning.</summary>
internal sealed class OverlayFrameWaiter:IDisposable
{
    private Func<TimeSpan,bool>? _highResolutionWait;
    private readonly Action<TimeSpan> _fallback;
    private IDisposable? _owner;
    private bool _disposed;
    public string Mode=>_highResolutionWait is null?"ordinary":"high-resolution";

    public OverlayFrameWaiter(Func<TimeSpan,bool>? highResolutionWait,Action<TimeSpan> fallback,IDisposable? owner)
    {_highResolutionWait=highResolutionWait;_fallback=fallback;_owner=owner;}

    public static OverlayFrameWaiter CreateWindows()
    {
        var timer=NativeOverlayTimer.TryCreate();
        Func<TimeSpan,bool>? wait=timer is null?null:timer.TryWait;
        return new(wait,delay=>Thread.Sleep(Math.Max(1,(int)Math.Ceiling(delay.TotalMilliseconds))),timer);
    }

    public void Wait(TimeSpan delay)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(delay<=TimeSpan.Zero)return;
        delay=delay>TimeSpan.FromMilliseconds(20)?TimeSpan.FromMilliseconds(20):delay;
        if(_highResolutionWait is not null)
        {
            try {if(_highResolutionWait(delay))return;}
            catch(Exception error) when(error is not OutOfMemoryException)
            {Log.Debug("Optional overlay timer failed: {ErrorType}",error.GetType().Name);}
            _highResolutionWait=null;
            Log.Warning("Optional overlay timer degraded to ordinary wait; recording is unchanged.");
        }
        _fallback(delay);
    }
    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;Interlocked.Exchange(ref _owner,null)?.Dispose();
    }

    private sealed class NativeOverlayTimer:IDisposable
    {
        private readonly SafeWaitHandle _handle;
        private NativeOverlayTimer(SafeWaitHandle handle)=>_handle=handle;
        public static NativeOverlayTimer? TryCreate()
        {
            if(!OperatingSystem.IsWindows())return null;
            try
            {
                var handle=CreateWaitableTimerExW(0,0,2,0x1F0003);
                if(!handle.IsInvalid)return new(handle);
                handle.Dispose();
            }
            catch(Exception error) when(error is DllNotFoundException or EntryPointNotFoundException)
            {Log.Debug("High-resolution overlay timer unavailable: {ErrorType}",error.GetType().Name);}
            return null;
        }
        public bool TryWait(TimeSpan delay)
        {
            var due=-Math.Max(1,delay.Ticks);
            return SetWaitableTimer(_handle,ref due,0,0,0,false)&&WaitForSingleObject(_handle,100)==0;
        }
        public void Dispose()=>_handle.Dispose();
        [DllImport("kernel32.dll",EntryPoint="CreateWaitableTimerExW",ExactSpelling=true,SetLastError=true)]
        private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes,nint name,uint flags,uint access);
        [DllImport("kernel32.dll",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(SafeWaitHandle timer,ref long due,int period,nint completion,nint argument,
            [MarshalAs(UnmanagedType.Bool)] bool resume);
        [DllImport("kernel32.dll",SetLastError=true)]
        private static extern uint WaitForSingleObject(SafeWaitHandle handle,uint milliseconds);
    }
}
