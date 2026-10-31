// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Enums;

namespace ScreenRecorder.Media.Tests;

public sealed class LinuxPortalSourcesTests
{
    [Fact]
    public void CancelledPreparationCannotReplaceAnExistingRestorationCredential()
    {
        var sources=new LinuxPortalSources();var view=sources.Store(Selection("original"));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>sources.Store(Selection("cancelled"),cancelled.Token));
        using var grant=sources.BeginRestore(view.SourceId,2,2);Assert.Equal("original",grant.Token);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ActualEngineRequiresRotatedGrantBeforeReadyAndReleasesItOnDispose(bool provideGrant)
    {
        if (OperatingSystem.IsWindows()) return; // owned Unix protocol child, not a physical PipeWire fixture
        var root = Path.Combine(Path.GetTempPath(), "opencam-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var sources = new LinuxPortalSources();
            var view = sources.Store(Selection());
            var helper = Path.Combine(root, "helper");
            File.WriteAllText(helper, "#!/bin/sh\nIFS= read -r request\nprintf '%s' \"$request\" > \"$0.request\"\n" +
                (provideGrant ? "printf '%s\\n' 'OCOBS {\"event\":\"portal_grant\",\"backend\":\"pipewire\",\"sourceId\":\"" + view.SourceId + "\",\"width\":2,\"height\":2,\"restoreToken\":\"rotated\"}' >&2\n" : "") +
                "printf 'MKV'\nprintf '%s\\n' 'OCOBS {\"event\":\"ready\",\"protocol\":1,\"backend\":\"synthetic\",\"encoder\":\"cpu\"}' >&2\n" +
                "IFS= read -r stop\nprintf '%s\\n' 'OCOBS {\"event\":\"stopped\"}' >&2\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var output = new MemoryStream();
            await using (var engine = new ObsScreenRecorderEngine(helper, root, (_,_,_) => sources.Monitor,
                setup: new LinuxPortalCaptureSetup(sources))) {
                engine.UseSyntheticCaptureSource = true; engine.OutputStream = output;
                if (!provideGrant) {
                    await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", new RecordingConfiguration { Fps=20, AudioSource=AudioSourceType.None }, new(0,0,2,2)));
                    Assert.False(engine.IsRunning);
                    return;
                }
                await engine.StartRecordingAsync("unused", new RecordingConfiguration { Fps=20, AudioSource=AudioSourceType.None }, new(0,0,2,2));
                Assert.True(engine.IsRunning);
                var sent = File.ReadAllText(helper + ".request");
                Assert.Contains("\"portalRestoreToken\":\"first\"", sent);
                Assert.Throws<IOException>(() => sources.BeginRestore(view.SourceId,2,2));
                await engine.StopRecordingAsync();
            }
            using var next = sources.BeginRestore(view.SourceId, 2, 2);
            Assert.Equal("rotated", next.Token);
        } finally { Directory.Delete(root, true); }
    }
    private static LinuxPortalSelection Selection(string token = "first") => LinuxPortalSelection.Parse(
        "OCPORTAL {\"event\":\"selected\",\"backend\":\"pipewire\",\"sourceId\":\"portal:" + new string('a', 64) +
        "\",\"restoreToken\":\"" + token + "\",\"width\":2,\"height\":2,\"previewWidth\":2,\"previewHeight\":2}",
        [0,0,0,255, 0,0,0,255, 0,0,0,255, 0,0,0,255]);

    [Fact]
    public void RestoreConsumesOnceAndRotatesForNextSegment()
    {
        var sources = new LinuxPortalSources();
        var view = sources.Store(Selection());
        Assert.DoesNotContain("first", System.Text.Json.JsonSerializer.Serialize(view));
        using (var grant = sources.BeginRestore(view.SourceId, 2, 2)) {
            Assert.Equal("first", grant.Token);
            Assert.Throws<IOException>(() => sources.BeginRestore(view.SourceId, 2, 2));
            grant.Rotate(view.SourceId, 2, 2, "second");
            Assert.Throws<IOException>(() => grant.Rotate(view.SourceId, 2, 2, "third"));
        }
        using var next = sources.BeginRestore(view.SourceId, 2, 2);
        Assert.Equal("second", next.Token);
    }

    [Fact]
    public void FailedOrChangedSourceCannotRestoreConsumedCredential()
    {
        var sources = new LinuxPortalSources();
        var view = sources.Store(Selection());
        Assert.Throws<IOException>(() => sources.BeginRestore(view.SourceId, 4, 2));
        using (var grant = sources.BeginRestore(view.SourceId, 2, 2))
            Assert.Throws<IOException>(() => grant.Rotate("portal:" + new string('b', 64), 2, 2, "other"));
        Assert.Throws<IOException>(() => sources.BeginRestore(view.SourceId, 2, 2));
        sources.Store(Selection("new-selection"));
        using var retry = sources.BeginRestore(view.SourceId, 2, 2);
        Assert.Equal("new-selection", retry.Token);
    }

    [Fact]
    public void ActiveRecordingPreventsReplacementAndPublicMonitorIsSourceLocal()
    {
        var sources = new LinuxPortalSources();
        var view = sources.Store(Selection());
        var monitor = sources.Monitor!;
        Assert.False(monitor.IsPrimary);
        Assert.Equal(0, monitor.Bounds.X); Assert.Equal(0, monitor.Bounds.Y);
        using var grant = sources.BeginRestore(view.SourceId, 2, 2);
        Assert.Throws<IOException>(() => sources.Store(Selection("replacement")));
    }
}
