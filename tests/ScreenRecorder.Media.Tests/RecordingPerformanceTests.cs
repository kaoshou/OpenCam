using ScreenRecorder.Media.Capture;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class RecordingPerformanceTests
{
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
