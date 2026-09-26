// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Xml.Linq;

namespace ScreenRecorder.Media.Tests;

public class MonitorIdentificationLayoutTests
{
    [Fact]
    public void IdentifyDisplays_IsCompactAndSharesTheMonitorSelectorRow()
    {
        var xamlPath = RepositoryTestFiles.Find("src/ScreenRecorder.UI/Views/MainWindow.axaml");
        var document = XDocument.Load(xamlPath);
        var monitorSelector = document.Descendants()
            .Single(element => element.Name.LocalName == "ComboBox" &&
                               element.Attribute("ItemsSource")?.Value == "{Binding AvailableMonitors}");
        var row = Assert.IsType<XElement>(monitorSelector.Parent);
        var identifyButton = Assert.Single(
            row.Elements(),
            element => element.Name.LocalName == "Button" &&
                       element.Attribute("Click")?.Value == "OnIdentifyDisplaysClicked");

        Assert.Equal("Grid", row.Name.LocalName);
        Assert.Equal("Auto,*,Auto", row.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("2", identifyButton.Attribute("Grid.Column")?.Value);
        Assert.Equal("{Binding CanSelectMonitor}", identifyButton.Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding Strings[IdentifyDisplaysTooltip]}",
            identifyButton.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("{Binding Strings[IdentifyDisplays]}",
            identifyButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.InRange(double.Parse(identifyButton.Attribute("Width")!.Value), 24, 36);
    }
}
