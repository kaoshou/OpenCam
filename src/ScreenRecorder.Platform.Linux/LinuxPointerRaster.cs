// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using ScreenRecorder.Core.Models;
namespace ScreenRecorder.Platform.Linux;

// Core X11 24-bit ZPixmap plus a binary bounding shape. No compositor or
// full-screen backing buffer is required for the small teaching pointer.
internal sealed record LinuxPointerRaster(byte[] Bgrx,byte[] Rectangles,int HotspotX,int HotspotY)
{
    internal static bool SupportsVisual(byte kind,uint red,uint green,uint blue)=>kind==4&&red==0xff0000&&green==0xff00&&blue==0xff;
    public static LinuxPointerRaster Create(TeachingPointerBitmap art)
    {
        if(art.Width is <1 or >256||art.Height is <1 or >256||art.Pixels.Length!=art.Width*art.Height*4)
            throw new ArgumentException("Unsupported pointer raster.");
        var pixels=art.Pixels.Span;var bgrx=new byte[pixels.Length];
        for(var i=0;i<pixels.Length;i+=4){bgrx[i]=pixels[i+2];bgrx[i+1]=pixels[i+1];bgrx[i+2]=pixels[i];}
        using var rectangles=new MemoryStream();Span<byte> rectangle=stackalloc byte[8];
        for(var y=0;y<art.Height;y++)for(var x=0;x<art.Width;) {
            if(pixels[(y*art.Width+x)*4+3]<128){x++;continue;}
            var start=x;while(x<art.Width&&pixels[(y*art.Width+x)*4+3]>=128)x++;
            BinaryPrimitives.WriteInt16LittleEndian(rectangle,(short)start);
            BinaryPrimitives.WriteInt16LittleEndian(rectangle[2..],(short)y);
            BinaryPrimitives.WriteUInt16LittleEndian(rectangle[4..],(ushort)(x-start));
            BinaryPrimitives.WriteUInt16LittleEndian(rectangle[6..],1);rectangles.Write(rectangle);
        }
        if(rectangles.Length==0)throw new ArgumentException("Pointer is not visible.");
        return new(bgrx,rectangles.ToArray(),(int)Math.Round(art.HotspotX),(int)Math.Round(art.HotspotY));
    }
}
