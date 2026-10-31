// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Platform.Linux;

namespace ScreenRecorder.Media.Tests;

public sealed class LinuxSourceSelectionServiceTests
{
    [Fact]
    public async Task LostBeginReplyCanCancelOnlyItsOperationWithoutKnowingServerIdentity()
    {
        var lease=new RecordingCaptureLease();
        await using var service=new LinuxSourceSelectionService(lease,()=>true,async ct=>{await Task.Delay(Timeout.Infinite,ct);throw new InvalidOperationException();});
        var id=Guid.NewGuid();await service.BeginAsync(id); // Simulate reply lost before the caller learns the server id.
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.DispatchAsync(new(){MessageType="CancelLinuxSourceSelection",PayloadJson=System.Text.Json.JsonSerializer.Serialize(new LinuxSourceSelectionRequest(Guid.NewGuid()))}));
        var response=await service.DispatchAsync(new(){MessageType="CancelLinuxSourceSelection",PayloadJson=System.Text.Json.JsonSerializer.Serialize(new LinuxSourceSelectionRequest(id))});
        Assert.True(response.Success);
        for(var i=0;i<100&&lease.IsOwned;i++)await Task.Delay(10);
        Assert.False(lease.IsOwned);
    }
    [Fact]
    public async Task PermissionWaitDoesNotBlockPollingAndHoldsActualCaptureLease()
    {
        var lease = new RecordingCaptureLease();
        var ready = new TaskCompletionSource<LinuxPortalSelectionView>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new LinuxSourceSelectionService(lease, () => true, ct => ready.Task.WaitAsync(ct));
        var id = Guid.NewGuid();
        var status = await service.BeginAsync(id);
        Assert.Equal("selecting", status.Phase);
        Assert.True(lease.IsOwned);
        Assert.Equal("selecting", service.Read(id, status.ServerInstanceId).Phase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.AcquireAsync(Guid.NewGuid(), CancellationToken.None).AsTask());
        ready.SetResult(new("portal:" + new string('a',64),2,2,2,2,"AA=="));
        for (var i=0; i<100 && service.Read(id,status.ServerInstanceId).Phase=="selecting"; i++) await Task.Delay(10);
        Assert.Equal("ready", service.Read(id,status.ServerInstanceId).Phase);
        Assert.False(lease.IsOwned);
    }
    [Fact]
    public async Task CancelIsScopedAndReleasesLeaseBeforeRetry()
    {
        var lease = new RecordingCaptureLease();
        await using var service = new LinuxSourceSelectionService(lease, () => true, async ct => { await Task.Delay(Timeout.Infinite,ct); throw new InvalidOperationException(); });
        var id=Guid.NewGuid(); var status=await service.BeginAsync(id);
        Assert.Throws<InvalidOperationException>(() => service.Cancel(Guid.NewGuid(),status.ServerInstanceId));
        service.Cancel(id,status.ServerInstanceId);
        for (var i=0;i<100 && service.Read(id,status.ServerInstanceId).Phase=="selecting";i++) await Task.Delay(10);
        Assert.Equal("cancelled",service.Read(id,status.ServerInstanceId).Phase);
        Assert.False(lease.IsOwned);
    }
    [Fact]
    public async Task ActiveRecordingRefusesSourceReplacement()
    {
        await using var service = new LinuxSourceSelectionService(new(), () => false, _ => throw new InvalidOperationException("must not select"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BeginAsync(Guid.NewGuid()));
    }
}
