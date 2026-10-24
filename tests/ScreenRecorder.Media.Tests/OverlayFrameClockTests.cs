using System.Reflection;
using ScreenRecorder.Platform.Windows.Cursor;

namespace ScreenRecorder.Media.Tests;

public class OverlayFrameClockTests
{
    private static Type Required(string name)
    {
        var type=typeof(WindowsCursorHighlightService).Assembly.GetType("ScreenRecorder.Platform.Windows.Cursor."+name);
        Assert.NotNull(type);return type;
    }
    private sealed class Clock
    {
        public long Now;
        public List<TimeSpan> Waits=new();
        private readonly object _clock;
        public Clock()
        {
            _clock=Activator.CreateInstance(Required("OverlayFrameClock"),
                [60000L,(Func<long>)(()=>Now),(Action<TimeSpan>)(delay=>{Waits.Add(delay);Now+=(long)Math.Round(delay.TotalSeconds*60000);})])!;
        }
        public void Wait()=>_clock.GetType().GetMethod("WaitNext")!.Invoke(_clock,null);
        public void Note(bool moved)=>_clock.GetType().GetMethod("NoteUpdate")!.Invoke(_clock,[moved]);
        public T Metric<T>(string name)
        {
            var snapshot=_clock.GetType().GetMethod("Snapshot")!.Invoke(_clock,null)!;
            return (T)snapshot.GetType().GetProperty(name)!.GetValue(snapshot)!;
        }
    }

    [Fact]
    public void WorkTimeIsSubtractedRatherThanAddedToEveryFrame()
    {
        var c=new Clock{Now=300};c.Wait();
        Assert.InRange(Assert.Single(c.Waits).TotalMilliseconds,11.66,11.68);
        Assert.Equal(1000,c.Now);
        c.Now+=300;c.Wait();
        Assert.Equal(2000,c.Now);
        Assert.Equal(2,c.Waits.Count);
    }
    [Fact]
    public void LateFramesSkipDeadlinesWithoutImmediateCatchUpBurst()
    {
        var c=new Clock{Now=4500};c.Wait();
        Assert.Empty(c.Waits);Assert.Equal(3,c.Metric<long>("MissedDeadlines"));
        c.Wait();Assert.Equal(5000,c.Now);
        Assert.InRange(Assert.Single(c.Waits).TotalMilliseconds,8.32,8.35);
    }
    [Fact]
    public void PollAndMovementCadenceIsBoundedWithoutRecordingCursorPositions()
    {
        var c=new Clock();c.Note(true);c.Now=1000;c.Note(false);c.Now=3000;c.Note(true);
        Assert.Equal(3,c.Metric<long>("Samples"));Assert.Equal(2,c.Metric<long>("MovedUpdates"));
        Assert.Equal(25,c.Metric<double>("MeanIntervalMs"),3);
        Assert.InRange(c.Metric<double>("MaxIntervalMs"),33.32,33.35);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimerFailureDegradesToBoundedWaitAndReleasesOwnership(bool throws)
    {
        var attempts=0;var waits=new List<TimeSpan>();var owner=new Lease();
        var type=Required("OverlayFrameWaiter");
        var waiter=Activator.CreateInstance(type,[(Func<TimeSpan,bool>)(_=>{
            attempts++;if(throws)throw new IOException("injected timer failure");return false;
        }),(Action<TimeSpan>)(d=>waits.Add(d)),owner])!;
        try
        {
            type.GetMethod("Wait")!.Invoke(waiter,[TimeSpan.FromSeconds(1)]);
            type.GetMethod("Wait")!.Invoke(waiter,[TimeSpan.FromMilliseconds(5)]);
            Assert.Equal(1,attempts);Assert.Equal(2,waits.Count);
            Assert.Equal(TimeSpan.FromMilliseconds(20),waits[0]);Assert.Equal(TimeSpan.FromMilliseconds(5),waits[1]);
            Assert.Equal("ordinary",type.GetProperty("Mode")!.GetValue(waiter));
        }
        finally {((IDisposable)waiter).Dispose();((IDisposable)waiter).Dispose();}
        Assert.Equal(1,owner.Disposals);
    }
    private sealed class Lease:IDisposable {public int Disposals;public void Dispose()=>Disposals++;}
}
