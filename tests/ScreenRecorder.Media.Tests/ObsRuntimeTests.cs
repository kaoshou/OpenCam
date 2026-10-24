// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Media.Tests;

public sealed class ObsRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opencam-obs-runtime-" + Guid.NewGuid().ToString("N"));
    private const string Platform = "osx-arm64";
    private const string Helper = "Contents/MacOS/OpenCam.ObsCapture";

    public ObsRuntimeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AcceptsOnlyEnabledPinnedRuntimeWithCompleteDigestInventory()
    {
        WriteManifest();
        var runtime = ObsRuntime.TryLoad(_root, Platform);
        Assert.NotNull(runtime);
        Assert.Equal(Path.Combine(_root, "Contents", "MacOS", "OpenCam.ObsCapture"), runtime.HelperPath);
        Assert.Contains("sck", runtime.Backends);
        File.AppendAllText(Path.Combine(_root, Helper), "changed");
        Assert.Null(ObsRuntime.TryLoad(_root, Platform));
    }

    [Theory]
    [InlineData(false, 1, "33.0.0", "osx-arm64")]
    [InlineData(true, 2, "33.0.0", "osx-arm64")]
    [InlineData(true, 1, "32.0.0", "osx-arm64")]
    [InlineData(true, 1, "33.0.0", "win-x64")]
    public void RejectsDisabledStaleOrWrongPlatform(bool enabled, int protocol, string version, string platform)
    {
        WriteManifest(enabled, protocol, version, platform);
        Assert.Null(ObsRuntime.TryLoad(_root, Platform));
    }

    [Fact]
    public void RejectsUnlistedRuntimeCodeAndMissingFiles()
    {
        WriteManifest();
        File.WriteAllText(Path.Combine(_root, "unlisted.dylib"), "code");
        Assert.Null(ObsRuntime.TryLoad(_root, Platform));
        File.Delete(Path.Combine(_root, "unlisted.dylib"));
        File.Delete(Path.Combine(_root, Helper));
        Assert.Null(ObsRuntime.TryLoad(_root, Platform));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("nested/../../escape")]
    [InlineData("nested\\escape")]
    public void RejectsNonPortableOrEscapingManifestPaths(string path)
    {
        WriteManifest(extra: path);
        Assert.Null(ObsRuntime.TryLoad(_root, Platform));
    }

    [UnixOnlyFact]
    public void RejectsSymlinkedRuntimeFiles()
    {
        if (OperatingSystem.IsWindows()) return;
        WriteManifest();
        var target = Path.Combine(_root, Helper);
        File.Move(target, target + ".original");
        File.CreateSymbolicLink(target, target + ".original");
        Assert.Null(ObsRuntime.TryLoad(_root, Platform));
    }

    private void WriteManifest(bool enabled = true, int protocol = 1, string version = "33.0.0",
        string platform = Platform, string? extra = null)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(_root, Helper), "helper");
        var files = new Dictionary<string, string> { [Helper] = Convert.ToHexString(SHA256.HashData("helper"u8)) };
        if (extra is not null) files[extra] = new string('0', 64);
        File.WriteAllText(Path.Combine(_root, "runtime.json"), JsonSerializer.Serialize(new
        {
            enabled, protocol, obsVersion = version, platform, sourceRevision = "72e86655715d9a25d69ce446aa4dd44ba58e10d2", files,
            capabilities = new { backends = new[] { "sck" }, audio = new[] { "none", "silence", "system", "microphone", "both" },
                encoders = new[] { "cpu", "apple" } }
        }));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
