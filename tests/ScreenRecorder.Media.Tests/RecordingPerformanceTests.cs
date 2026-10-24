using ScreenRecorder.Media.Capture;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class RecordingPerformanceTests
{
    [Fact]
    public void NativeProgressPreservesCadenceAndOwnedWriteMetricsWithoutInventingFfmpegValues()
    {
        var metrics = new RecordingPerformance();
        metrics.ReadNativeProgress(30, 1_000_000);
        metrics.NoteWrite(4096, 2);
        metrics.Snapshot(1);
        metrics.ReadNativeProgress(150, 5_000_000);
        var report = metrics.Snapshot(5);
        Assert.Equal(150, report.Frames);
        Assert.Equal(30, report.OutputFps);
        Assert.Equal(30, report.RecentOutputFps);
        Assert.Equal(5, report.MediaSeconds);
        Assert.Equal(4096, report.OutputBytes);
        Assert.Equal(2, report.WriteWaitMs);
        Assert.Null(report.FfmpegFps);
        Assert.Null(report.Speed);
        Assert.Null(report.DuplicatedFrames);
        Assert.Null(report.DroppedFrames);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void NativeProgressRejectsInvalidCounters(long frames, long mediaUs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingPerformance().ReadNativeProgress(frames, mediaUs));
    }
    [Fact]
    public void RecentRateExposesSlowMotionWithoutStartupAveraging()
    {
        var metrics=new RecordingPerformance();
        metrics.ReadLine("frame=30");metrics.Snapshot(5);
        metrics.ReadLine("frame=50");var report=metrics.Snapshot(10);
        var recent=report.GetType().GetProperty("RecentOutputFps");
        Assert.NotNull(recent);
        Assert.Equal(4d,recent.GetValue(report));
        Assert.Equal(5d,report.GetType().GetProperty("RecentSeconds")!.GetValue(report));
        Assert.Equal(5d,report.OutputFps);
        Assert.Null(metrics.Snapshot(10).GetType().GetProperty("RecentOutputFps")!.GetValue(metrics.Snapshot(10)));
        metrics.ReadLine("frame=2");
        Assert.Null(recent.GetValue(metrics.Snapshot(11)));
    }
    [Fact]
    public void ReportsOutputCadenceAndWriteWaitWithoutInventingCaptureDrops()
    {
        var metrics = new RecordingPerformance();
        foreach (var line in new[] { "frame=120", "fps=29.75", "speed=0.98x", "out_time_us=4000000", "dup_frames=8", "drop_frames=2", "progress=continue" })
            metrics.ReadLine(line);
        metrics.NoteWrite(4096, 12);
        metrics.NoteWrite(4096, 28);
        var report = metrics.Snapshot(5);
        Assert.Equal(120, report.Frames);
        Assert.Equal(24, report.OutputFps);
        Assert.Equal(29.75, report.FfmpegFps);
        Assert.Equal(0.98, report.Speed);
        Assert.Equal(4, report.MediaSeconds);
        Assert.Equal(8, report.DuplicatedFrames);
        Assert.Equal(2, report.DroppedFrames);
        Assert.Equal(8192, report.OutputBytes);
        Assert.Equal(40, report.WriteWaitMs);
        Assert.Equal(28, report.MaxWriteWaitMs);
    }

    [Fact]
    public void MissingOrInvalidProgressRemainsUnknownAndNewMeasurementHasNoPriorFrames()
    {
        var metrics = new RecordingPerformance();
        foreach (var line in new[] { "fps=N/A", "speed=NaNx", "frame=-1", "out_time_us=Infinity", "drop_frames=oops", "unrelated=42" })
            metrics.ReadLine(line);
        var report = metrics.Snapshot(0);
        Assert.Null(report.OutputFps);
        Assert.Null(report.FfmpegFps);
        Assert.Null(report.Speed);
        Assert.Null(report.Frames);
        Assert.Null(report.DroppedFrames);
        Assert.Null(report.DuplicatedFrames);
        Assert.Null(report.MediaSeconds);
    }
}
