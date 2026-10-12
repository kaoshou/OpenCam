using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;
public partial class RecordingEncoderLifecycleTests
{
    [Fact]
    public async Task ProjectTotalIncludesLiveOnlyOnceAndSurvivesReopenAndTrim()
    {
        await using var scope = new RecordingScope();
        await using var project = new ProjectRecordingCoordinator(scope.Recorder, new JsonProjectStore(), new ProjectProbeStub());
        long Total() => project.TotalRecordingTicks;
        long Session() => project.SessionRecordingTicks;
        Assert.True((await project.CreateAsync(scope.Configuration.OutputDirectory, "total")).Success);
        Assert.Equal(0, Total());
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Equal(10000000, Total());
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        Assert.Equal(10000000, Total());
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Equal(20000000, Total());
        Assert.Equal(20000000, Session());
        Assert.True((await project.PauseAsync(Guid.NewGuid())).Success);
        Assert.Equal(20000000, Total());
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Equal(30000000, Total());
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        Assert.Equal(30000000, Total());
        Assert.True((await project.StartAsync(scope.Configuration, Guid.NewGuid())).Success);
        Assert.Equal(40000000, Total());
        Assert.Equal(10000000, Session());
        Assert.True((await project.FinishAsync(Guid.NewGuid())).Success);
        var path = Path.Combine(project.ProjectDirectory!, "project.opencam");
        Assert.True((await project.CloseAsync()).Success);
        Assert.Equal(0, Total());
        Assert.Equal(0, Session());
        Assert.True((await project.OpenAsync(path)).Success);
        Assert.Equal(40000000, Total());
        Assert.True((await project.ApplyEditAsync(new ProjectClipEdit.Remove(project.Current!.Clips[0].Id), project.Current.Revision, Guid.NewGuid())).Success);
        Assert.Equal(30000000, Total());
    }
}
