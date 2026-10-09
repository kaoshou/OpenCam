using ScreenRecorder.Infrastructure.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectSummaryTests
{
    [Fact]
    public async Task SummaryReadsNameWhileProjectIsOpenWithoutTakingItsLock()
    {
        var temp = Directory.CreateTempSubdirectory("opencam-summary-");
        try
        {
            await using var handle = await new JsonProjectStore().CreateAsync(temp.FullName, "課程一");
            var summary = await JsonProjectStore.ReadSummaryAsync(Path.Combine(handle.ProjectDirectory, "project.opencam"));
            Assert.Equal("課程一", summary.Name);
            Assert.Contains("課程一", summary.ToString());
            Assert.Equal(0, handle.Current.Revision);
        }
        finally { temp.Delete(true); }
    }
}
