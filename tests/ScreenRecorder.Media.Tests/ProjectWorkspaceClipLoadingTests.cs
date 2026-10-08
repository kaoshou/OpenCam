// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectWorkspaceClipLoadingTests
{
    [Fact]
    public async Task SafetyStopPollKeepsEditingDisabledUntilAllClipPagesAreCurrent()
    {
        var client = new ClipClient();
        var vm = new ProjectWorkspaceViewModel(client);
        vm.ApplyReply(new(true, null, client.State with { Mode = ProjectMode.Recording }));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.BeforePage = async request => {
            if (request.Offset == 100) { entered.SetResult(); await release.Task; }
        };
        var poll = vm.PollRecordingAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(vm.CanEdit);
            Assert.False(vm.CanRecord);
            Assert.Empty(vm.Clips);
        }
        finally { release.SetResult(); await poll; }
        Assert.Equal(200, vm.Clips.Count);
        Assert.True(vm.CanEdit);
    }

    [Fact]
    public async Task RefreshLoadsTwoHundredClipsAndPreservesSelectionPastFirstPage()
    {
        var client = new ClipClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        Assert.Equal(200, vm.Clips.Count);
        vm.SelectedClip = vm.Clips[150];
        var selectedId = vm.SelectedClip.Id;
        await vm.RefreshAsync();
        Assert.Equal(selectedId, vm.SelectedClip?.Id);
        Assert.Equal(client.Clips.Select(c => c.Id), vm.Clips.Select(c => c.Id));
        Assert.All(client.Requests, request => Assert.InRange(request.Limit, 1, 100));
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("project")]
    [InlineData("count")]
    [InlineData("duplicate")]
    [InlineData("short")]
    [InlineData("unconfirmed")]
    public async Task InvalidLaterPageDoesNotPublishPartialOrMixedSnapshot(string fault)
    {
        var client = new ClipClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        var old = vm.Clips.ToArray();
        client.Fault = fault;
        await vm.RefreshAsync();
        Assert.True(vm.StatusUnconfirmed);
        Assert.False(vm.CanEdit);
        Assert.Equal(old, vm.Clips.ToArray());
        client.Fault = null;
        await vm.RefreshAsync();
        Assert.False(vm.StatusUnconfirmed);
        Assert.True(vm.CanEdit);
    }

    [Fact]
    public async Task ClosedProjectClearsPreviousClipsAndSelection()
    {
        var client = new ClipClient();
        var vm = new ProjectWorkspaceViewModel(client);
        await vm.RefreshAsync();
        Assert.NotEmpty(vm.Clips);
        client.State = ProjectSnapshot.Closed;
        await vm.RefreshAsync();
        Assert.Empty(vm.Clips);
        Assert.Null(vm.SelectedClip);
    }

    internal sealed class ClipClient : IProjectClient
    {
        public ProjectSnapshot State = new(Guid.NewGuid(), "Long project", "/test", 7, 7, ProjectMode.Ready, 200, false, false);
        public ProjectClip[] Clips { get; } = Enumerable.Range(0, 200).Select(i => new ProjectClip {
            Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), InPts = 0, OutPts = 1000, Name = $"Clip {i + 1}" }).ToArray();
        public List<ProjectRequest> Requests { get; } = [];
        public string? Fault { get; set; }
        public Func<ProjectRequest, Task>? BeforePage { get; set; }
        public async Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
        {
            if (command == "CloseProject") { State = ProjectSnapshot.Closed; return new(true, null, State); }
            if (command == "GetProjectStatus") return new(true, null, State);
            Assert.Equal("GetProjectClips", command);
            if (BeforePage is not null) await BeforePage(request);
            Requests.Add(request);
            var state = State;
            var clips = Clips.Skip(request.Offset).Take(request.Limit).ToArray();
            var unconfirmed = false;
            if (request.Offset >= 100)
            {
                if (Fault == "revision") state = state with { Revision = 8 };
                if (Fault == "project") state = state with { ProjectId = Guid.NewGuid() };
                if (Fault == "count") state = state with { ClipCount = 201 };
                if (Fault == "duplicate") clips[0] = Clips[0];
                if (Fault == "short") clips = [];
                if (Fault == "unconfirmed") unconfirmed = true;
            }
            return new(true, null, state, clips, unconfirmed);
        }
    }
}
