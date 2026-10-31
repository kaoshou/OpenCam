// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
namespace ScreenRecorder.Media.Tests;
public sealed class LinuxHotkeyMapTests
{
    [Theory]
    [InlineData(0x7B,0xFFC9u)] [InlineData(0x70,0xFFBEu)] [InlineData(0x52,0x72u)] [InlineData(0x20,0x20u)]
    [InlineData(0x2D,0xFF63u)] [InlineData(0,0u)]
    public void ExistingConfiguredKeysMapToX11Keysyms(int key,uint symbol)=>Assert.Equal(symbol,LinuxHotkeyMap.Keysym(key));
    [Theory]
    [InlineData(0u,0u)] [InlineData(2u,4u)] [InlineData(6u,5u)] [InlineData(1u,8u)] [InlineData(8u,64u)]
    public void ConfiguredModifierBitsUseX11Masks(uint modifiers,uint expected)=>Assert.Equal(expected,LinuxHotkeyMap.Modifiers(modifiers));
    [Fact]
    public void UnknownModifierBitsCannotBecomeAnUnmodifiedGlobalGrab()=>Assert.Throws<ArgumentException>(()=>LinuxHotkeyMap.Modifiers(16));
}
