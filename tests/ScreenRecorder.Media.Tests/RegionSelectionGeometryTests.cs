// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public class RegionSelectionGeometryTests
{
    [Fact]
    public void FromWindow_ConvertsRetinaSizeToEvenPhysicalPixels()
    {
        var result = RegionSelectionGeometry.FromWindow(
            new PixelPoint(100, 80),
            new Size(641.5, 480.5),
            2.0);

        Assert.Equal(
            new CaptureRegion(100, 80, 1282, 960),
            result);
    }

    [Fact]
    public void ToLogicalSize_ConvertsPhysicalSelectionBeforeWindowIsShown()
    {
        var result = RegionSelectionGeometry.ToLogicalSize(
            pixelWidth: 1280,
            pixelHeight: 720,
            renderScaling: 2.0);

        Assert.Equal(new Size(640, 360), result);
    }

    [Fact]
    public void ClampToBounds_ClampsToOneDisplay()
    {
        var result = RegionSelectionGeometry.ClampToBounds(
            new CaptureRegion(6300, 1000, 400, 300),
            new CaptureRegion(4480, 0, 1920, 1080));

        Assert.Equal(
            new CaptureRegion(6000, 780, 400, 300),
            result);
    }

    [Fact]
    public void ResizeWindow_SouthEastDragExpandsFromFixedTopLeftCorner()
    {
        var result = RegionSelectionGeometry.ResizeWindow(
            new PixelRect(100, 80, 1280, 720),
            new PixelPoint(1380, 800),
            new PixelPoint(1580, 900),
            WindowEdge.SouthEast,
            minimumWidth: 320,
            minimumHeight: 240);

        Assert.Equal(new PixelRect(100, 80, 1480, 820), result);
    }

    [Fact]
    public void ResizeWindow_NorthWestDragMovesOriginAndKeepsOppositeCornerFixed()
    {
        var result = RegionSelectionGeometry.ResizeWindow(
            new PixelRect(100, 80, 1280, 720),
            new PixelPoint(100, 80),
            new PixelPoint(300, 180),
            WindowEdge.NorthWest,
            minimumWidth: 320,
            minimumHeight: 240);

        Assert.Equal(new PixelRect(300, 180, 1080, 620), result);
    }

    [Fact]
    public void ResizeWindow_WestDragStopsAtMinimumWidthAndKeepsRightEdgeFixed()
    {
        var result = RegionSelectionGeometry.ResizeWindow(
            new PixelRect(100, 80, 1280, 720),
            new PixelPoint(100, 440),
            new PixelPoint(1500, 440),
            WindowEdge.West,
            minimumWidth: 320,
            minimumHeight: 240);

        Assert.Equal(new PixelRect(1060, 80, 320, 720), result);
    }
}
