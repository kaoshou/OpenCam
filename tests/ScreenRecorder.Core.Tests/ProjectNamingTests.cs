using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectNamingTests
{
    [Fact]
    public void RenameCanUndoRedoWithoutChangingIdentityOrSources()
    {
        var project = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "Original" };
        var history = new ProjectEditHistory(project);
        history.RenameProject("教學 / Lesson");
        Assert.Equal("教學 / Lesson", history.Current.Name);
        Assert.Equal(project.ProjectId, history.Current.ProjectId);
        Assert.Equal(project.Sources, history.Current.Sources);
        history.Undo();
        Assert.Equal("Original", history.Current.Name);
        history.Redo();
        Assert.Equal("教學 / Lesson", history.Current.Name);
        Assert.Equal(3, history.Current.Revision);
    }

    [Theory]
    [InlineData("../CON:lesson\\test?", "CON-lesson-test")]
    [InlineData("教學 影片", "教學 影片")]
    [InlineData("...", "OpenCam")]
    public void FileStemNeverUsesPathSyntax(string name, string expected)
    {
        Assert.Equal(expected, ProjectNaming.FileStem(name));
    }

    [Fact]
    public void InvalidRenameLeavesHistoryUntouched()
    {
        var history = new ProjectEditHistory(new() { ProjectId = Guid.NewGuid(), Name = "Original" });
        Assert.ThrowsAny<Exception>(() => history.RenameProject(" "));
        Assert.Equal("Original", history.Current.Name);
        Assert.False(history.CanUndo);
    }
}
