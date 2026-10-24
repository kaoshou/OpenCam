// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.macOS;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ScreenRecorder.Media.Tests;

public sealed class MacNativePointerCaptureTests
{
    private static readonly CaptureRegion Bounds = new(-1800, 100, 640, 480);
    private static readonly CaptureRegion DisplayBounds = new(-1920, 0, 1920, 1080);
    private static CaptureSelection Selection => new(CaptureBackend.ScreenCaptureKit, CaptureFallbackReason.None,
        "123", null, null, Bounds) { OutputBounds = DisplayBounds };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditableModeExplicitlyOptsIntoClockProtocolAndPreservesLegacyArguments(bool editable)
    {
        var start = MacOsNativeVideoCapture.BuildStartInfo("native-helper", new()
        { EditablePointerEnabled = editable, Fps = 60, VideoQualityPreset = "Ultra", CursorEffect = CursorEffectMode.Hidden },
            123, Bounds, DisplayBounds);
        string[] legacy = ["123", "120", "100", "640", "480", "1920", "1080", "60", "10000", "0"];
        Assert.Equal(editable ? [.. legacy, "1"] : legacy, start.ArgumentList);
        Assert.True(start.RedirectStandardError && start.RedirectStandardInput && start.RedirectStandardOutput);
    }

    [Fact]
    public void PointerAndClockMetadataReachPrivateSubscribersWithoutEnteringLogs()
    {
        var sink = new Sink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using var process = new Process();
        var capture = new MacOsNativeVideoCapture(process, Selection, true, logger);
        var events = new List<NativePointerEvent>();
        capture.PointerMetadata += (_, metadata) => events.Add(metadata);
        capture.HandleDiagnosticLine("POINTER v1 8000000 321 123 1 1");
        capture.HandleDiagnosticLine("FRAME_CLOCK v1 0 8000000 7999000 1");
        capture.HandleDiagnosticLine("CAPTURE delivered=1 repeatedSamples=0");
        Assert.Equal(new PointerSample(8000000, 321, 123, true, 1), events[0].Pointer);
        Assert.Equal(8000000, events[1].Frame!.Value.PresentationMicroseconds);
        Assert.Single(sink.Messages);
        Assert.Contains("CAPTURE delivered=1", sink.Messages[0]);
        Assert.DoesNotContain(sink.Messages, x => x.Contains("POINTER") || x.Contains("FRAME_CLOCK") || x.Contains("321"));
    }

    [Theory]
    [InlineData("POINTER_ERROR v1 queue-overflow")]
    [InlineData("POINTER v1 100 9999 0 1 0")]
    [InlineData("FRAME_CLOCK v1 0 100 101 1")]
    public void PointerFailureLatchesIncompleteAndKeepsVideoDiagnosticsAlive(string failure)
    {
        var sink = new Sink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using var process = new Process();
        var capture = new MacOsNativeVideoCapture(process, Selection, true, logger);
        var incomplete = new List<string>(); var metadata = new List<NativePointerEvent>(); var videoFailures = new List<string>();
        capture.PointerIncomplete += (_, value) => incomplete.Add(value);
        capture.PointerMetadata += (_, value) => metadata.Add(value);
        capture.CaptureFailed += (_, value) => videoFailures.Add(value);
        capture.HandleDiagnosticLine(failure);
        capture.HandleDiagnosticLine("POINTER_ERROR v1 event-tap-disabled");
        capture.HandleDiagnosticLine("POINTER v1 101 3 4 1 0");
        capture.HandleDiagnosticLine("STOP frames=60 dropped=0");
        Assert.Single(incomplete);
        Assert.DoesNotContain("9999", incomplete[0]);
        Assert.Empty(metadata); Assert.Empty(videoFailures);
        Assert.Single(sink.Messages); Assert.Contains("STOP frames=60", sink.Messages[0]);
    }

    [Fact]
    public void LegacyModeAlsoDiscardsUnexpectedPrivateMetadata()
    {
        var sink = new Sink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using var process = new Process();
        var capture = new MacOsNativeVideoCapture(process, Selection, false, logger);
        capture.PointerMetadata += (_, _) => Assert.Fail("legacy mode must not publish editable metadata");
        capture.HandleDiagnosticLine("POINTER v1 1 321 123 1 0");
        capture.HandleDiagnosticLine("FRAME_CLOCK v1 0 100 99 1");
        Assert.Empty(sink.Messages);
    }

    [MacOsOnlyFact]
    public async Task ClockReadyHandshakeIsRequiredAndProducerIsReaped()
    {
        foreach (var ready in new[] { "READY h264-matroska-clock-v1", "READY h264-matroska-v1" })
        {
            var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("printf '%s\\n' \"$1\" >&2; read command");
            start.ArgumentList.Add("opencam-pointer-test"); start.ArgumentList.Add(ready);
            var process = Process.Start(start)!;
            if (ready.EndsWith("clock-v1", StringComparison.Ordinal))
            {
                await using var capture = await MacOsNativeVideoCapture.PrepareProcessAsync(process, Selection, true, default);
                await capture.StopAsync();
            }
            else await Assert.ThrowsAsync<IOException>(() =>
                MacOsNativeVideoCapture.PrepareProcessAsync(process, Selection, true, default));
        }
    }

    private sealed class Sink : ILogEventSink
    {
        public List<string> Messages { get; } = [];
        public void Emit(LogEvent logEvent) => Messages.Add(logEvent.RenderMessage());
    }
}
