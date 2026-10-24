// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public class RecordingDisplayIdentityTests
{
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
}
