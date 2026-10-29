// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Media.Tests;

public sealed class LinuxRuntimeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "opencam-linux-runtime-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public void LinuxManifestRequiresExactInventoryAndLinuxBackends()
    {
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        var helper = Path.Combine(root, "bin", "OpenCam.ObsCapture");
        File.WriteAllText(helper, "linux-helper");
        void Manifest(string[] backends) => File.WriteAllText(Path.Combine(root, "runtime.json"), JsonSerializer.Serialize(new {
            enabled = true, protocol = 1, obsVersion = "33.0.0", platform = "linux-x64",
            sourceRevision = ObsRuntime.PinnedSourceRevision,
            files = new Dictionary<string, string> { ["bin/OpenCam.ObsCapture"] = Convert.ToHexString(SHA256.HashData("linux-helper"u8)) },
            capabilities = new { backends, audio = new[] { "none", "system", "microphone", "both", "silence" }, encoders = new[] { "cpu" } }
        }));
        Manifest(["x11", "pipewire"]);
        var runtime = ObsRuntime.TryLoad(root, "linux-x64");
        Assert.NotNull(runtime); Assert.Equal(helper, runtime.HelperPath);
        Assert.Null(ObsRuntime.TryLoad(root, "win-x64"));
        Manifest(["sck"]); Assert.Null(ObsRuntime.TryLoad(root, "linux-x64"));
        Manifest(["x11"]); File.AppendAllText(helper, "tampered");
        Assert.Null(ObsRuntime.TryLoad(root, "linux-x64"));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
