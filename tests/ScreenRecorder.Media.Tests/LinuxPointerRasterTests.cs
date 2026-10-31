// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Core.Models;
namespace ScreenRecorder.Media.Tests;
public sealed class LinuxPointerRasterTests
{
    [Theory]
    [InlineData(4,0xff0000u,0xff00u,0xffu,true)]
    [InlineData(3,0xff0000u,0xff00u,0xffu,false)]
    [InlineData(4,0xffu,0xff00u,0xff0000u,false)]
    [InlineData(4,0xf800u,0x7e0u,0x1fu,false)]
    public void UnsupportedNativeVisualNeverUsesTheBgrxRaster(byte kind,uint red,uint green,uint blue,bool supported)=>
        Assert.Equal(supported,LinuxPointerRaster.SupportsVisual(kind,red,green,blue));
    [Fact]
    public void ShapedRasterKeepsVisibleArtworkColorsAndExcludesTransparentPixels()
    {
        byte[] pixels=[10,20,30,255, 99,88,77,0, 40,50,60,200, 1,2,3,10];
        var raster=LinuxPointerRaster.Create(new(pixels,2,2,1,1,2));
        Assert.Equal(new byte[]{30,20,10,0, 77,88,99,0, 60,50,40,0, 3,2,1,0},raster.Bgrx);
        Assert.Equal(new byte[]{0,0,0,0,1,0,1,0, 0,0,1,0,1,0,1,0},raster.Rectangles);
    }
    [Theory]
    [InlineData(TeachingPointerStyle.WarmIvory,TeachingPointerSize.Small)]
    [InlineData(TeachingPointerStyle.HighContrast,TeachingPointerSize.Large)]
    public void OriginalArtworkUsesTipHotspotAndBoundedNativeRequest(TeachingPointerStyle style,TeachingPointerSize size)
    {
        var art=TeachingPointerArtwork.Get(new(style,size));var raster=LinuxPointerRaster.Create(art);
        Assert.Equal((int)Math.Round(art.HotspotX),raster.HotspotX);
        Assert.Equal((int)Math.Round(art.HotspotY),raster.HotspotY);
        Assert.True(raster.Bgrx.Length<200000);Assert.NotEmpty(raster.Rectangles);
    }
}
