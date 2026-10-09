using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectClipListDragTests
{
    private static ProjectWorkspaceViewModel Model()
    {
        var vm = new ProjectWorkspaceViewModel(new NoClient());
        vm.ApplyReply(new(true, null, new(Guid.NewGuid(), "test", null, 1, 1, ProjectMode.Ready, 4, false, false)));
        var group = Guid.NewGuid();
        for (var i = 0; i < 4; i++) vm.Clips.Add(new() { Id = Guid.NewGuid(), Name = "clip", GroupId = i is 1 or 2 ? group : null });
        return vm;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompactAndThumbnailUseSameMove(bool compact)
    {
        var vm = Model(); vm.IsClipListCompact = compact;
        Assert.True(vm.TryResolveDrop(vm.Clips[3].Id, vm.Clips[0].Id, false, out var target));
        Assert.Equal(vm.Clips[3].Id, target.ClipId);
        Assert.Equal(vm.Clips[0].Id, target.BeforeClipId);
    }

    [Fact]
    public void DropInsideGroupSnapsToGroupBoundary()
    {
        var vm = Model();
        Assert.True(vm.TryResolveDrop(vm.Clips[3].Id, vm.Clips[2].Id, false, out var target));
        Assert.Equal(vm.Clips[1].Id, target.BeforeClipId);
        Assert.True(vm.TryResolveDrop(vm.Clips[0].Id, vm.Clips[1].Id, true, out target));
        Assert.Equal(vm.Clips[3].Id, target.BeforeClipId);
    }

    [Fact]
    public void GroupMovesAsUnit()
    {
        var vm = Model();
        Assert.True(vm.TryResolveDrop(vm.Clips[2].Id, null, true, out var target));
        Assert.Equal(vm.Clips[1].Id, target.ClipId);
        Assert.Null(target.BeforeClipId);
    }

    [Fact]
    public void NoOpAddsNoUndo()
    {
        var vm = Model();
        Assert.False(vm.TryResolveDrop(vm.Clips[3].Id, null, true, out _));
        Assert.False(vm.TryResolveDrop(vm.Clips[1].Id, vm.Clips[2].Id, true, out _));
    }

    [Fact]
    public void BusyTransitionCancelsDrop()
    {
        var vm = Model();
        vm.IsBusy = true;
        Assert.False(vm.TryResolveDrop(vm.Clips[3].Id, vm.Clips[0].Id, false, out _));
    }

    private sealed class NoClient : IProjectClient
    {
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("Target calculation must not mutate the project.");
    }
}
