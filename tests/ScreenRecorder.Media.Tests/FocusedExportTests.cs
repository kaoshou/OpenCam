using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Projects;
using System.Text.Json;

namespace ScreenRecorder.Media.Tests;

public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task FocusedSelectionSeeksAndMergeWithPreviousCanUndo()
    {
        await using var scope = new RecordingScope();
        await using var coordinator = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        var dispatcher = new ProjectIpcDispatcher(coordinator);
        var vm = new ProjectWorkspaceViewModel(new ProjectClient((command, request, ct) =>
            dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) }),
            async (request, ct) => JsonSerializer.Deserialize<ProjectReply>((await dispatcher.DispatchAsync(
                new() { MessageType = "GetProjectStatus", PayloadJson = JsonSerializer.Serialize(request) })).DataJson!)!));
        await vm.CreateAsync(scope.Configuration.OutputDirectory, "select");
        await vm.StartAsync(scope.Configuration); await vm.PauseAsync();
        await vm.StartAsync(scope.Configuration); await vm.PauseAsync();
        Assert.Equal(2, vm.Clips.Count);
        await vm.GroupWithPreviousAsync();
        Assert.Null(vm.Clips[0].GroupId); // No selection: no edit.
        vm.SelectedClip = vm.Clips[1];
        await vm.GroupWithPreviousAsync();
        Assert.NotNull(vm.Clips[0].GroupId);
        Assert.Equal(vm.Clips[0].GroupId, vm.Clips[1].GroupId);
        await vm.UndoAsync();
        Assert.All(vm.Clips, clip => Assert.Null(clip.GroupId));
        vm.SelectedClip = vm.Clips[0];
        await vm.GroupWithNextAsync();
        Assert.NotNull(vm.Clips[0].GroupId);
        Assert.Equal(vm.Clips[0].GroupId, vm.Clips[1].GroupId);
        await vm.UndoAsync();
        vm.SelectedClip = vm.Clips[1];
        Assert.Equal(0, vm.PlayheadTicks); // Passive selection does not seek.
        await vm.SelectClipForPlaybackAsync(vm.Clips[1].Id);
        Assert.Equal(vm.TimelineClips[1].StartTicks, vm.PlayheadTicks);
        await vm.MergeWithPreviousAsync();
        Assert.Null(vm.Error);
        Assert.Single(vm.Clips);
        Assert.Equal(0, vm.PlayheadTicks);
        vm.ClipMuted = true;
        await vm.ApplyPropertiesAsync();
        Assert.True(vm.ClipMuted);
        vm.ClipMuted = false;
        await vm.ApplyPropertiesAsync();
        Assert.All(vm.Clips[0].Parts, part => Assert.False(part.Muted));
        await vm.UndoAsync();
        await vm.UndoAsync();
        await vm.UndoAsync();
        Assert.Equal(2, vm.Clips.Count);
        await vm.FinishAsync();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FocusedNamedExportPublishesChosenNameWithoutOverwriting(bool collision)
    {
        await using var scope = new RecordingScope();
        var folder = scope.Configuration.OutputDirectory;
        await using var owner = await new JsonProjectStore().CreateAsync(folder, "named");
        var exporter = new ControlledContentExporter();
        using var job = new RecordingContentExportJob(exporter, owner, owner.Current, folder,
            (_, _, _, _) => Task.CompletedTask, fileName: "chosen.mp4");
        await exporter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var generated = Path.Combine(folder, "generated.mp4");
        await File.WriteAllTextAsync(generated, "verified test bytes");
        var chosen = Path.Combine(folder, "chosen.mp4");
        if (collision) await File.WriteAllTextAsync(chosen, "keep me");
        exporter.Complete.TrySetResult(new(true, generated, null));
        await job.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(job.Status.State == (collision ? RecordingExportState.Failed : RecordingExportState.Succeeded), job.Status.Error);
        Assert.Equal(collision ? "keep me" : "verified test bytes", await File.ReadAllTextAsync(chosen));
        Assert.Equal(collision, File.Exists(generated));
        if (!collision) Assert.Equal("chosen.mp4", Path.GetFileName(job.Status.FinalPath));
    }

    [Fact]
    public void FocusedCompoundWaveformPreservesTimeAndAudio()
    {
        var data = ProjectWaveformService.Combine([
            new(true, false, 48000, [new(-1, 1, .5f)]),
            new(false, true, 48000, [new(0, 0, 0)])]);
        Assert.Equal(96000, data.SampleCount);
        Assert.True(data.HasAudio); Assert.False(data.IsSilent);
        Assert.Equal(256, data.Buckets.Length);
        Assert.Equal(.5f, data.Buckets[0].Rms);
        Assert.Equal(0, data.Buckets[^1].Rms);
    }
}
