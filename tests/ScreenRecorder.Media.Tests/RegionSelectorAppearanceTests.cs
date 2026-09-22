// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public class RegionSelectorAppearanceTests
{
    [Fact]
    public void InstructionBackdrop_ContrastsTextWithoutBlockingTransparentDragArea()
    {
        var xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml"));
        var document = XDocument.Load(xamlPath);
        var dragArea = document.Descendants()
            .Single(element => element.Name.LocalName == "Border" &&
                               element.Attribute("Grid.Row")?.Value == "1");
        var instructionBackdrop = Assert.Single(dragArea.Elements()
            .Where(element => element.Name.LocalName == "Border"));

        Assert.Equal("Transparent", dragArea.Attribute("Background")?.Value);
        Assert.Equal("OnDragMovePointerPressed", dragArea.Attribute("PointerPressed")?.Value);
        Assert.Equal("False", instructionBackdrop.Attribute("IsHitTestVisible")?.Value);
        Assert.StartsWith("#D", instructionBackdrop.Attribute("Background")?.Value);
        Assert.Equal("Center", instructionBackdrop.Attribute("HorizontalAlignment")?.Value);
        Assert.All(instructionBackdrop.Descendants().Where(element => element.Name.LocalName == "TextBlock"),
            text => Assert.Equal("Wrap", text.Attribute("TextWrapping")?.Value));
    }

    [Fact]
    public void MacOs_UsesTransparentCompositionWithoutBlur()
    {
        var levels = RegionSelectorAppearance.TransparencyLevels(isMacOS: true);

        Assert.Equal(new[] { WindowTransparencyLevel.Transparent }, levels);
        Assert.Equal(
            Colors.Transparent,
            RegionSelectorAppearance.FallbackColor(isMacOS: true));
        Assert.DoesNotContain(WindowTransparencyLevel.Blur, levels);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void NativeWindowTransparency_IsAppliedOnlyOnMacOs(
        bool isMacOS,
        bool expected)
    {
        Assert.Equal(
            expected,
            RegionSelectorAppearance.RequiresNativeWindowTransparency(
                isMacOS));
    }
}
