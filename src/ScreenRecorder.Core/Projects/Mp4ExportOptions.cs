// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Serialization;
namespace ScreenRecorder.Core.Projects;

public enum Mp4ExportQuality { Original = 0, Standard = 1, Compact = 2 }
public enum Mp4ExportResolution { Original = 0, FullHd = 1, Hd = 2 }

public sealed record Mp4ExportOptions(Mp4ExportQuality Quality = Mp4ExportQuality.Original,
    Mp4ExportResolution Resolution = Mp4ExportResolution.Original)
{
    [JsonIgnore]
    public bool IsOriginal => Quality == Mp4ExportQuality.Original && Resolution == Mp4ExportResolution.Original;
    [JsonIgnore]
    public int VideoCrf => Quality switch
    {
        Mp4ExportQuality.Original => 18,
        Mp4ExportQuality.Standard => 23,
        Mp4ExportQuality.Compact => 28,
        _ => throw new InvalidDataException("Invalid MP4 export quality.")
    };
    public void Validate()
    {
        if (!Enum.IsDefined(Quality) || !Enum.IsDefined(Resolution))
            throw new InvalidDataException("Invalid MP4 export settings.");
    }

    public ProjectCanvas OutputCanvas(ProjectCanvas canvas)
    {
        Validate();
        if (Resolution == Mp4ExportResolution.Original) return canvas;
        if (canvas.Width < 2 || canvas.Height < 2)
            throw new InvalidDataException("H.264 export needs dimensions of at least two pixels.");
        var landscape = canvas.Width >= canvas.Height;
        var longEdge = Resolution == Mp4ExportResolution.FullHd ? 1920 : 1280;
        var shortEdge = Resolution == Mp4ExportResolution.FullHd ? 1080 : 720;
        var maximumWidth = landscape ? longEdge : shortEdge;
        var maximumHeight = landscape ? shortEdge : longEdge;
        var ratio = Math.Min(1, Math.Min((double)maximumWidth / canvas.Width, (double)maximumHeight / canvas.Height));
        return canvas with { Width = Math.Max(2, (int)(canvas.Width * ratio) / 2 * 2),
            Height = Math.Max(2, (int)(canvas.Height * ratio) / 2 * 2) };
    }
}
