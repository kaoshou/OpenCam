// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class PointerRasterizerTests
{
    [Fact]
    public void ValidTinySourceCropAndZoomMayNeedMoreThanSixteenRasterUnits()
    {
        var geometry = ProjectPointerGeometry.Create(new() { Width = 19, Height = 19 },
            new() { Crop = 40, Scale = 2 }, new(108, 108, new(30, 1)), 108, 108);
        Assert.Equal(19, geometry.RasterScale);
        var point = geometry.Map(new(0, 9, 9, true, 0));
        var pixels = new byte[108 * 108 * 4];
        PointerRasterizer.Composite(pixels, 108, 108, point, new(ProjectPointerStyle.Dot), geometry.RasterScale);
        Assert.True(pixels[(54 * 108 + 54) * 4 + 3] > 0);
        Assert.Equal(0, pixels[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => PointerRasterizer.Composite(pixels, 108, 108, point, new(), 32.01));
    }

    [Theory]
    [InlineData(ProjectPointerStyle.Stick)]
    [InlineData(ProjectPointerStyle.Arrow)]
    [InlineData(ProjectPointerStyle.Dot)]
    public void HotspotIsVisibleAndBackgroundOutsidePointerStaysTransparent(ProjectPointerStyle style)
    {
        var pixels = new byte[192 * 108 * 4];
        PointerRasterizer.Composite(pixels, 192, 108, new PointerSample(0, 40, 40, true, 0), new(style, 100), 10);
        Assert.True(pixels[(40 * 192 + 40) * 4 + 3] > 0);
        Assert.Equal(0, pixels[(10 * 192 + 10) * 4 + 3]);
        Assert.Equal(0, pixels[(95 * 192 + 180) * 4 + 3]);
    }

    [Fact]
    public void ScalingDoesNotMoveTipAndStickDoesNotExtendUpLeftOfTip()
    {
        foreach (var percent in new[] {50,100,200})
        {
            var pixels = new byte[192 * 108 * 4];
            PointerRasterizer.Composite(pixels,192,108,new(0,40,40,true,0),new(ProjectPointerStyle.Stick,percent),10);
            Assert.True(pixels[(40*192+40)*4+3]>0);
            Assert.Equal(0,pixels[(35*192+35)*4+3]);
        }
    }

    [Fact]
    public void HiddenOrMissingSamplesLeaveEveryPixelUnchanged()
    {
        var pixels = Enumerable.Repeat((byte)71,64*64*4).ToArray();
        var before = pixels.ToArray();
        PointerRasterizer.Composite(pixels,64,64,new(0,20,20,true,0),new(ProjectPointerStyle.Hidden,100));
        PointerRasterizer.Composite(pixels,64,64,null,new());
        PointerRasterizer.Composite(pixels,64,64,new(0,0,0,false,0),new());
        Assert.Equal(before,pixels);
    }

    [Fact]
    public void EdgeClippingDoesNotWriteOutsideFrameOrAcceptInvalidBuffers()
    {
        var pixels = new byte[64*64*4];
        PointerRasterizer.Composite(pixels,64,64,new(0,63,63,true,0),new(),10);
        Assert.True(pixels[^1]>0);
        Assert.Throws<ArgumentException>(() => PointerRasterizer.Composite(new byte[10],64,64,new(0,0,0,true,0),new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => PointerRasterizer.Composite(pixels,64,64,new(0,double.NaN,0,true,0),new()));
    }
}
