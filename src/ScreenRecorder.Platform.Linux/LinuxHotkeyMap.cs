// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Platform.Linux;
internal static class LinuxHotkeyMap
{
    internal static uint Keysym(int key)=>key switch {
        >=0x70 and <=0x87=>(uint)(0xFFBE+key-0x70),>=0x41 and <=0x5A=>(uint)(key+32),>=0x30 and <=0x39=>(uint)key,
        0x20=>0x20,0x1B=>0xFF1B,0x2D=>0xFF63,0x24=>0xFF50,0x23=>0xFF57,0x21=>0xFF55,0x22=>0xFF56,_=>0};
    internal static ushort Modifiers(uint bits)
    {
        if((bits&~15u)!=0)throw new ArgumentException("Unsupported shortcut modifiers.");
        return (ushort)(((bits&1)!=0?8:0)|((bits&2)!=0?4:0)|((bits&4)!=0?1:0)|((bits&8)!=0?64:0));
    }
}
