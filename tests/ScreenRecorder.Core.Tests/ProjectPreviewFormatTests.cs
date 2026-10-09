using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Core.Tests;

public class ProjectPreviewFormatTests
{
    [Theory]
    [InlineData(1920,1080,0,640,360)] [InlineData(1920,1080,1,1280,720)]
    [InlineData(1920,1080,2,1920,1080)] [InlineData(3840,2160,3,3840,2160)]
    [InlineData(1080,1920,1,720,1280)] [InlineData(1000,1000,1,720,720)]
    [InlineData(320,180,2,320,180)] [InlineData(6000,1000,1,1280,213)]
    [InlineData(1001,501,3,1001,501)]
    public void PreviewDimensionsPreserveCanvasAndNeverUpscale(int w,int h,int quality,int wantW,int wantH)
    {
        var size = ProjectPreviewFormat.Resolve(new(w,h,new(30,1)), (ProjectPreviewQuality)quality);
        Assert.Equal(wantW, size.Width); Assert.Equal(wantH, size.Height);
        Assert.Equal(wantW * wantH * 4, size.ByteCount);
    }
    [Fact]
    public void OriginalOversizeAndInvalidQualityAreRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => ProjectPreviewFormat.Resolve(new(16384,16384,new(30,1)),ProjectPreviewQuality.Original));
        Assert.ThrowsAny<ArgumentException>(() => ProjectPreviewFormat.Resolve(new(1920,1080,new(30,1)),(ProjectPreviewQuality)999));
    }
}
