// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
namespace ScreenRecorder.Media.Tests;
public sealed class LinuxCursorCapabilityTests
{
    [Theory]
    [InlineData(true,CursorEffectMode.PresentationStick,false,true)]
    [InlineData(false,CursorEffectMode.PresentationStick,false,false)]
    [InlineData(true,CursorEffectMode.Default,true,false)]
    [InlineData(true,CursorEffectMode.HighlightHalo,false,false)]
    [InlineData(false,CursorEffectMode.Default,false,true)]
    [InlineData(false,CursorEffectMode.Hidden,false,true)]
    public void OnlyConfirmedX11LivePointerPathIsAllowed(bool x11,CursorEffectMode mode,bool editable,bool expected)=>
        Assert.Equal(expected,LinuxRecordingEngine.SupportsCursor(new(){CursorEffect=mode,EditablePointerEnabled=editable},x11));
}
