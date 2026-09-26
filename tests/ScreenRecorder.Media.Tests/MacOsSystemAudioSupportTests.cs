// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public class MacOsSystemAudioSupportTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "OpenCamSupport_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RequiresMacOs13AndBundledHelper()
    {
        Directory.CreateDirectory(_directory);
        Assert.False(MacOsSystemAudioSupport.IsSupported(
            new Version(12, 6),
            _directory));
        Assert.False(MacOsSystemAudioSupport.IsSupported(
            new Version(13, 0),
            _directory));

        File.WriteAllText(
            Path.Combine(
                _directory,
                MacOsSystemAudioSupport.HelperFileName),
            "fixture");

        Assert.True(MacOsSystemAudioSupport.IsSupported(
            new Version(13, 0),
            _directory));
    }

    // The removed filesystem FIFO is covered by AnonymousAudioTransportTests
    // and capture lifecycle tests that require no filesystem audio endpoint.

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
