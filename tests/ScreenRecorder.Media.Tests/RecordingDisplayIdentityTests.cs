// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.macOS;
using System.Runtime.InteropServices;

namespace ScreenRecorder.Media.Tests;

public class RecordingDisplayIdentityTests
{
    [MacOsOnlyFact]
    public void MacNativeDisplayUuidRoundTripsThroughColorSync()
    {
        // Use the actual OS boundary, including when the main display is asleep.
        // Fake IMacOsDisplayApi fixtures cannot detect a wrong framework import.
        var displayId = NativeDisplayIdentity.CGMainDisplayID();
        Assert.NotEqual(0u, displayId);
        var identity = new CoreGraphicsDisplayApi().GetDisplayUuid(displayId);
        Assert.True(Guid.TryParse(identity, out _), "A real display must have a usable UUID.");
        Assert.Equal(identity, new CoreGraphicsDisplayApi().GetDisplayUuid(displayId));
        var text = NativeDisplayIdentity.CFStringCreateWithCString(0, identity!, 0x08000100);
        Assert.NotEqual(0, text);
        nint uuid = 0;
        try
        {
            uuid = NativeDisplayIdentity.CFUUIDCreateFromString(0, text);
            Assert.NotEqual(0, uuid);
            Assert.Equal(displayId, NativeDisplayIdentity.CGDisplayGetDisplayIDFromUUID(uuid));
        }
        finally
        {
            if (uuid != 0) NativeDisplayIdentity.CFRelease(uuid);
            NativeDisplayIdentity.CFRelease(text);
        }
    }

    [Fact]
    public void MacIdentityUsesMatchingGeometryNotStaleEnumerationIndex()
    {
        var api = new Displays(new(42, 1120, 0, 1920, 1080, 1920, 1080, false),
            new(91, 0, 0, 1120, 630, 2240, 1260, true));
        var service = new MacOsDisplayService(api);
        var stale = new MonitorInfo(1, "Mac Display 2", new(1120, 0, 1920, 1080), false, 1);
        Assert.Equal("UUID-42", service.GetRecordingDisplayIdentity(stale));
    }

    [Fact]
    public void MacInventedFallbackDisplayIsNeverUsedForNativeRecording()
    {
        var service = new MacOsDisplayService(new Displays());
        Assert.Null(service.GetRecordingDisplayIdentity(Assert.Single(service.GetMonitors())));
    }

    [Fact]
    public void MacAmbiguousMirrorGeometryDoesNotGuessAnIdentity()
    {
        var service = new MacOsDisplayService(new Displays(new(42,0,0,1920,1080,1920,1080,true),
            new(91,0,0,1920,1080,1920,1080,false)));
        Assert.Null(service.GetRecordingDisplayIdentity(service.GetMonitors()[0]));
    }

    private sealed class Displays(params MacOsDisplaySnapshot[] snapshots) : IMacOsDisplayApi
    {
        public IReadOnlyList<MacOsDisplaySnapshot> GetActiveDisplays() => snapshots;
        public string? GetDisplayUuid(uint id) => "UUID-" + id;
    }

    private static class NativeDisplayIdentity
    {
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        internal static extern uint CGMainDisplayID();
        [DllImport("/System/Library/Frameworks/ColorSync.framework/ColorSync")]
        internal static extern uint CGDisplayGetDisplayIDFromUUID(nint uuid);
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        internal static extern nint CFStringCreateWithCString(nint allocator,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        internal static extern nint CFUUIDCreateFromString(nint allocator, nint text);
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        internal static extern void CFRelease(nint value);
    }
}
