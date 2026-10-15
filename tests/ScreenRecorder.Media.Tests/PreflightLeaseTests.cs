using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public class PreflightLeaseTests
{
    [Fact]
    public async Task CaptureCannotHaveTwoOwnersAndDisposingTwiceCannotReleaseAnotherOwner()
    {
        var capture = new RecordingCaptureLease();
        var first = await capture.AcquireAsync(Guid.NewGuid(), default);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await capture.AcquireAsync(Guid.NewGuid(), default));
        await first.DisposeAsync();
        await using var second = await capture.AcquireAsync(Guid.NewGuid(), default);
        await first.DisposeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await capture.AcquireAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task CancellationDoesNotReserveCapture()
    {
        var capture = new RecordingCaptureLease();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await capture.AcquireAsync(Guid.NewGuid(), canceled.Token));
        await using var acquired = await capture.AcquireAsync(Guid.NewGuid(), default);
        Assert.True(capture.IsOwned);
    }
}
