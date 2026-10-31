// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.UI.Services;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Media.Tests;
public sealed class LinuxPortalCropTests
{
    [Fact]
    public void DragMapsPreviewToSourcePixelsRatherThanDesktopCoordinates()
    {
        Assert.Equal(new CaptureRegion(480,160,1120,720),LinuxPortalCropGeometry.MapDrag(1920,1080,480,270,120,40,400,220));
        Assert.Equal(new CaptureRegion(480,160,1120,720),LinuxPortalCropGeometry.MapDrag(1920,1080,480,270,400,220,120,40));
    }
    [Fact]
    public void DragClampsToAuthorizedSourceAndProducesEvenOutput()
    {
        Assert.Equal(new CaptureRegion(0,0,1920,1080),LinuxPortalCropGeometry.MapDrag(1920,1080,480,270,-20,-40,700,500));
        var r=LinuxPortalCropGeometry.MapDrag(1919,1079,480,269,0,0,480,269);
        Assert.Equal(new CaptureRegion(0,0,1918,1078),r);
    }
    [Fact]
    public void InvalidGeometryAndNonfiniteInputCannotCreateCaptureSelection()
    {
        Assert.Throws<ArgumentException>(()=>LinuxPortalCropGeometry.MapDrag(0,1080,480,270,0,0,1,1));
        Assert.Throws<ArgumentException>(()=>LinuxPortalCropGeometry.MapDrag(1920,1080,480,270,double.NaN,0,1,1));
    }
}
