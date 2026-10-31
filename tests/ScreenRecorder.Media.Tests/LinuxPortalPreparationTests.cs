// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Security.Cryptography;
using System.Diagnostics;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Platform.Linux;

namespace ScreenRecorder.Media.Tests;

public sealed class LinuxPortalPreparationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "opencam-portal-contract-" + Guid.NewGuid().ToString("N"));
    private string Fixture(string script)
    {
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        var path = Path.Combine(root, "bin", "OpenCam.ObsCapture");
        File.WriteAllText(path, "#!/bin/sh\n" + script);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(root, "runtime.json"), JsonSerializer.Serialize(new {
            enabled = true, protocol = 1, obsVersion = "33.0.0", platform = "linux-x64", sourceRevision = ObsRuntime.PinnedSourceRevision,
            files = new Dictionary<string, string> { ["bin/OpenCam.ObsCapture"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) },
            capabilities = new { backends = new[] { "x11" }, audio = new[] { "none" }, encoders = new[] { "cpu" } }
        }));
        return path;
    }

    [Fact]
    public async Task ActualPrivatePipesProduceSelectionWithoutMediaFile()
    {
        if (OperatingSystem.IsWindows()) return; // Unix process-protocol fixture, not physical portal acceptance.
        Fixture("printf '%s\\n' '" + Selected() + "' >&2\nprintf '\\000\\000\\000\\377\\000\\000\\000\\377\\000\\000\\000\\377\\000\\000\\000\\377'\n");
        var selection = await LinuxPortalPreparation.PrepareAsync(root, CancellationToken.None);
        Assert.Equal("private-token", selection.RestoreToken);
        Assert.Equal(16, selection.Rgba.Length);
        Assert.Empty(Directory.GetFiles(root, "*.mkv", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ActualOversizedPipeFailsBoundedlyWithoutEchoingPayload()
    {
        if (OperatingSystem.IsWindows()) return;
        Fixture("head -c 600000 /dev/zero\n");
        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsAsync<IOException>(() => LinuxPortalPreparation.PrepareAsync(root, CancellationToken.None));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task CancellationReapsOwnedHelperRatherThanLeavingPermissionProcess()
    {
        if (OperatingSystem.IsWindows()) return;
        var pidFile = Path.Combine(Path.GetTempPath(), "opencam-portal-pid-" + Guid.NewGuid().ToString("N"));
        try {
            Fixture("echo $$ > '" + pidFile + "'\nexec sleep 30\n");
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LinuxPortalPreparation.PrepareAsync(root, cancel.Token));
            var pid = int.Parse(File.ReadAllText(pidFile).Trim());
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
        } finally { if (File.Exists(pidFile)) File.Delete(pidFile); }
    }
    [Fact]
    public async Task PreparationRejectsMissingRuntimeBeforeStartingIt()
    {
        var error = await Assert.ThrowsAsync<IOException>(() => LinuxPortalPreparation.PrepareAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), CancellationToken.None));
        Assert.DoesNotContain("private-token", error.Message);
    }
    private static string Selected(object? width = null, string token = "private-token", string? source = null) =>
        "OCPORTAL " + JsonSerializer.Serialize(new {
            @event = "selected", backend = "pipewire", sourceId = source ?? "portal:" + new string('a', 64),
            restoreToken = token, width = width ?? 2, height = 2, previewWidth = 2, previewHeight = 2
        });
    private static byte[] Frame() => [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];

    [Fact]
    public void RejectsPreviewWhoseSizeDoesNotMatchSourceScaling() =>
        Assert.Throws<IOException>(() => LinuxPortalSelection.Parse(Selected(width: 4), Frame()));

    [Fact]
    public void AcceptsBoundedOpaqueBlackFrameAndKeepsCredentialOutOfDisplayText()
    {
        var value = LinuxPortalSelection.Parse(Selected(), Frame());
        Assert.Equal(2, value.Width);
        Assert.Equal(2, value.Height);
        Assert.Equal("private-token", value.RestoreToken);
        Assert.DoesNotContain("private-token", value.ToString());
    }

    [Theory]
    [InlineData(0)] [InlineData(16385)]
    public void RejectsUnboundedGeometry(int width) =>
        Assert.Throws<IOException>(() => LinuxPortalSelection.Parse(Selected(width), Frame()));

    [Theory]
    [InlineData("")] [InlineData("secret\nline")]
    public void RejectsInvalidCredentialWithoutLeakingIt(string token)
    {
        var error = Assert.Throws<IOException>(() => LinuxPortalSelection.Parse(Selected(token: token), Frame()));
        Assert.DoesNotContain("restoreToken", error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public void RejectsIncompleteOrUndrawnPreviewAndDuplicateSelection()
    {
        Assert.Throws<IOException>(() => LinuxPortalSelection.Parse(Selected(), [0, 0, 0, 255]));
        Assert.Throws<IOException>(() => LinuxPortalSelection.Parse(Selected(), new byte[16]));
        Assert.Throws<IOException>(() => LinuxPortalSelection.Parse(Selected() + "\n" + Selected(), Frame()));
    }

    [Fact]
    public void RejectsLateSuccessAfterCancellationAndMalformedPayloadWithoutEchoingIt()
    {
        Assert.Throws<IOException>(() => LinuxPortalSelection.Parse("OCPORTAL {\"event\":\"error\",\"code\":\"portal_cancelled\"}\n" + Selected(), Frame()));
        var error = Assert.Throws<IOException>(() => LinuxPortalSelection.Parse("OCPORTAL private-token", []));
        Assert.DoesNotContain("private-token", error.ToString());
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
