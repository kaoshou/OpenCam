// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Media.Tests;

public partial class FFmpegStartupHandshakeTests
{
    private sealed class ModernPlan(CaptureBackend backend = CaptureBackend.DesktopDuplication) : ICapturePlanProvider
    {
        public Task<CaptureLaunchPlan> PrepareAsync(RecordingConfiguration config, CaptureRegion bounds,
            CaptureSelection? pinned, CancellationToken token) =>
            Task.FromResult(new CaptureLaunchPlan(new(backend,
                backend == CaptureBackend.Gdi ? CaptureFallbackReason.StartupFailed : CaptureFallbackReason.None, "screen", 1, 0, bounds), ""));
        public string BuildInputArguments(CaptureLaunchPlan plan, RecordingConfiguration config,
            bool hasDirectShowMic, string? sys, string? mic) => "";
    }

    [UnixOnlyFact]
    public async Task GdiFallbackPermissionFailureDoesNotTriggerCpuRetry()
    {
        var executable = CreateExecutable("echo 'Error opening output: Permission denied' >&2\nexit 1");
        try
        {
            await using var engine = new FFmpegScreenRecorderEngine(new StubProvider(), ffmpegPath: executable,
                capturePlanProvider: new ModernPlan(CaptureBackend.Gdi));
            await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartRecordingAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"), Config(), new(0, 0, 640, 480)));
        }
        finally { File.Delete(executable); }
    }

    [UnixOnlyFact]
    public async Task DdaInputMentionDoesNotTurnOutputPermissionErrorIntoCaptureFailure()
    {
        var executable = CreateExecutable("echo \"Input #0, lavfi, from 'ddagrab=output_idx=0':\" >&2\necho 'Failed to open output: Permission denied' >&2\nexit 1");
        try
        {
            await using var engine = new FFmpegScreenRecorderEngine(new StubProvider(), ffmpegPath: executable,
                capturePlanProvider: new ModernPlan());
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartRecordingAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"), Config(), new(0, 0, 640, 480)));
            Assert.IsNotType<CaptureStartupException>(error);
        }
        finally { File.Delete(executable); }
    }

    [UnixOnlyFact]
    public async Task IdentifiedDdaInitializationErrorAllowsCaptureFallback()
    {
        var executable = CreateExecutable("echo '[Parsed_ddagrab_0 @ 0x123] Failed to create D3D11VA device.' >&2\nexit 1");
        try
        {
            await using var engine = new FFmpegScreenRecorderEngine(new StubProvider(), ffmpegPath: executable,
                capturePlanProvider: new ModernPlan());
            await Assert.ThrowsAsync<CaptureStartupException>(() => engine.StartRecordingAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"), Config(), new(0, 0, 640, 480)));
        }
        finally { File.Delete(executable); }
    }
}
