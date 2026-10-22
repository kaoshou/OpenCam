// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class Mp4ExportOptionsTests
{
    [Theory]
    [InlineData(3840, 2160, 1, 1920, 1080)]
    [InlineData(3840, 2160, 2, 1280, 720)]
    [InlineData(2160, 3840, 1, 1080, 1920)]
    [InlineData(640, 360, 2, 640, 360)]
    [InlineData(1281, 721, 2, 1278, 720)]
    public void ResolutionBoundsNeverUpscaleAndPreserveTheFrameClock(int width, int height, int resolution,
        int expectedWidth, int expectedHeight)
    {
        var canvas = new ProjectCanvas(width, height, new(30000, 1001));
        var actual = new Mp4ExportOptions(Resolution: (Mp4ExportResolution)resolution).OutputCanvas(canvas);
        Assert.Equal(expectedWidth, actual.Width);
        Assert.Equal(expectedHeight, actual.Height);
        Assert.Equal(new ProjectRational(30000, 1001), actual.Fps);
        Assert.Equal(width, canvas.Width);
    }

    private static RecordingProject Project()
    {
        var source = new ProjectSource { Id = Guid.NewGuid(), SessionId = "session", RelativePath = "sources/a.mkv",
            FileSize = 5, Sha256 = new string('a', 64), Width = 1920, Height = 1080,
            VideoCodec = "h264", Timing = new(new(1, 1000), 0, 1000) };
        return new() { ProjectId = Guid.NewGuid(), Name = "export quality", Sessions = ["session"], Sources = [source],
            Clips = [new() { Id = Guid.NewGuid(), SourceId = source.Id, Name = "clip", OutPts = 1000 }] };
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    public void DifferentCompressionSettingsCannotReuseAnOriginalQualityReceipt(int quality, int resolution)
    {
        var project = Project();
        var options = JsonSerializer.Deserialize<ProjectExportOptions>(
            $"{{\"Mp4\":{{\"Quality\":{quality},\"Resolution\":{resolution}}}}}")!;
        Assert.NotEqual(ProjectExportFingerprint.Compute(project, new ProjectExportOptions()),
            ProjectExportFingerprint.Compute(project, options));
    }

    [Theory]
    [InlineData(99, 0, 0)]
    [InlineData(0, 99, 0)]
    [InlineData(1, 0, 1)]
    public void InvalidProfileOrGifWithMp4SettingsIsRejected(int quality, int resolution, int format)
    {
        var options = JsonSerializer.Deserialize<ProjectExportOptions>(
            $"{{\"Format\":{format},\"Mp4\":{{\"Quality\":{quality},\"Resolution\":{resolution}}}}}")!;
        Assert.Throws<InvalidDataException>(() => ProjectExportSelection.Create(Project(), options));
    }

    [Fact]
    public void ExplicitDefaultKeepsTheExistingOriginalExportFingerprint()
    {
        var project = Project();
        var options = JsonSerializer.Deserialize<ProjectExportOptions>("{\"Mp4\":{\"Quality\":0,\"Resolution\":0}}")!;
        Assert.Equal(ProjectExportFingerprint.Compute(project, ProjectExportFingerprint.Profile),
            ProjectExportFingerprint.Compute(project, options));
    }
}
