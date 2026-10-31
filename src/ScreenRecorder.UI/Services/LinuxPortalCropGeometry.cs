// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
namespace ScreenRecorder.UI.Services;

internal static class LinuxPortalCropGeometry
{
    public static CaptureRegion MapDrag(int width,int height,int previewWidth,int previewHeight,double x1,double y1,double x2,double y2)
    {
        if (width is <2 or >16384 || height is <2 or >16384 || previewWidth is <1 or >480 || previewHeight is <1 or >270 ||
            !double.IsFinite(x1)||!double.IsFinite(y1)||!double.IsFinite(x2)||!double.IsFinite(y2))
            throw new ArgumentException("Invalid authorized source geometry.");
        var left=(int)Math.Floor(Math.Clamp(Math.Min(x1,x2),0,previewWidth)*width/previewWidth);
        var top=(int)Math.Floor(Math.Clamp(Math.Min(y1,y2),0,previewHeight)*height/previewHeight);
        var right=(int)Math.Floor(Math.Clamp(Math.Max(x1,x2),0,previewWidth)*width/previewWidth);
        var bottom=(int)Math.Floor(Math.Clamp(Math.Max(y1,y2),0,previewHeight)*height/previewHeight);
        return new(left,top,(right-left)&~1,(bottom-top)&~1);
    }
}
