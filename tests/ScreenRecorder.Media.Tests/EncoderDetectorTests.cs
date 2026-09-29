// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.Encoders;
using Xunit;
using System.Diagnostics;
using ScreenRecorder.Core.Interfaces;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ScreenRecorder.Media.Tests;

public class EncoderDetectorTests
{
    [Fact]
    public async Task ExplicitCpu_DoesNotProbe()
    {
        using var scope = new DetectorScope();
        var selected = await scope.Detector.SelectAsync(HardwareEncoderType.SoftwareCpu);
        Assert.Equal(HardwareEncoderType.SoftwareCpu, selected.Encoder);
        Assert.Empty(scope.Runner.Calls);
    }

    [Theory]
    [InlineData(false, HardwareEncoderType.IntelQsv)]
    [InlineData(true, HardwareEncoderType.AppleVideoToolbox)]
    public async Task Auto_UsesPlatformOrderIncludingVideoToolbox(bool mac, HardwareEncoderType expected)
    {
        using var scope = new DetectorScope(mac);
        scope.Runner.AvailableCodec = mac ? "h264_videotoolbox" : "h264_qsv";
        Assert.Equal(expected, (await scope.Detector.SelectAsync(HardwareEncoderType.Auto)).Encoder);
        Assert.DoesNotContain(scope.Runner.Calls, x => x.Contains("h264_amf"));
        Assert.Equal(mac ? 1 : 2, scope.Runner.HardwareCalls);
    }

    [Fact]
    public async Task RequestedHardwareUnavailable_FallsBackWithReason()
    {
        using var scope = new DetectorScope();
        scope.Runner.AvailableCodec = "h264_qsv";
        var selected = await scope.Detector.SelectAsync(HardwareEncoderType.NvidiaNvenc);
        Assert.Equal(new EncoderSelection(HardwareEncoderType.SoftwareCpu,
            EncoderFallbackReason.RequestedHardwareUnavailable), selected);
        Assert.Equal(1, scope.Runner.HardwareCalls);
    }

    [Theory]
    [InlineData(true, 600)]
    [InlineData(false, 30)]
    public async Task Cache_ExpiresSuccessAndFailureSeparately(bool available, int seconds)
    {
        using var scope = new DetectorScope();
        scope.Runner.AvailableCodec = available ? "h264_qsv" : null;
        await scope.Detector.SelectAsync(HardwareEncoderType.IntelQsv);
        scope.Clock.Advance(TimeSpan.FromSeconds(seconds - 1));
        await scope.Detector.SelectAsync(HardwareEncoderType.IntelQsv);
        Assert.Equal(1, scope.Runner.HardwareCalls);
        scope.Clock.Advance(TimeSpan.FromSeconds(1));
        await scope.Detector.SelectAsync(HardwareEncoderType.IntelQsv);
        Assert.Equal(2, scope.Runner.HardwareCalls);
    }

    [Fact]
    public async Task StartupFailure_InvalidatesSuccessUntilCooldownEnds()
    {
        using var scope = new DetectorScope();
        scope.Runner.AvailableCodec = "h264_qsv";
        await scope.Detector.SelectAsync(HardwareEncoderType.IntelQsv);
        scope.Detector.ReportStartupFailure(HardwareEncoderType.IntelQsv);
        Assert.Equal(HardwareEncoderType.SoftwareCpu, (await scope.Detector.SelectAsync(HardwareEncoderType.IntelQsv)).Encoder);
        Assert.Equal(1, scope.Runner.HardwareCalls);
        scope.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(HardwareEncoderType.IntelQsv, (await scope.Detector.SelectAsync(HardwareEncoderType.IntelQsv)).Encoder);
        Assert.Equal(2, scope.Runner.HardwareCalls);
    }

    [Fact]
    public async Task ExecutableChangedAtSamePath_InvalidatesCandidates()
    {
        using var scope = new DetectorScope();
        scope.Runner.AvailableCodec = "h264_qsv";
        await scope.Detector.SelectAsync(HardwareEncoderType.IntelQsv);
        scope.Detector.ReportStartupFailure(HardwareEncoderType.IntelQsv);
        File.AppendAllText(scope.Executable, "updated binary");
        scope.Runner.Version = "ffmpeg version test-2";
        Assert.Equal(HardwareEncoderType.IntelQsv, (await scope.Detector.SelectAsync(HardwareEncoderType.IntelQsv)).Encoder);
        Assert.Equal(2, scope.Runner.HardwareCalls);
        Assert.Equal(2, scope.Runner.Calls.Count(x => x.Contains("-version")));
    }

    [Fact]
    public async Task CancelledCaller_DoesNotPoisonNextCaller()
    {
        using var scope = new DetectorScope();
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        scope.Runner.HardwareResponse = async token =>
        {
            if (first)
            {
                first = false;
                entered.SetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { }
                return ProbeResult(EncoderProbeStatus.Cancelled);
            }
            return ProbeResult(EncoderProbeStatus.Available);
        };
        var cancelled = scope.Detector.SelectAsync(HardwareEncoderType.IntelQsv, cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var next = scope.Detector.SelectAsync(HardwareEncoderType.IntelQsv);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(HardwareEncoderType.IntelQsv, (await next).Encoder);
        Assert.Equal(1, scope.Runner.MaxConcurrentCalls);
        Assert.Equal(2, scope.Runner.HardwareCalls);
    }

    [Fact]
    public async Task DetectionBudget_StopsLaunchingCandidates()
    {
        using var scope = new DetectorScope();
        scope.Runner.HardwareResponse = _ =>
        {
            // 3 seconds execution + 2 seconds cleanup per failed child.
            scope.Clock.Advance(TimeSpan.FromSeconds(5));
            return Task.FromResult(ProbeResult(EncoderProbeStatus.TimedOut));
        };
        var selected = await scope.Detector.SelectAsync(HardwareEncoderType.Auto);
        Assert.Equal(HardwareEncoderType.SoftwareCpu, selected.Encoder);
        Assert.Equal(2, scope.Runner.HardwareCalls);
        Assert.DoesNotContain(scope.Runner.Calls, x => x.Contains("h264_amf"));
    }

    [Fact]
    public async Task IncompleteCleanup_IsFatalAndNotCached()
    {
        using var scope = new DetectorScope();
        scope.Runner.HardwareResponse = _ => Task.FromResult(ProbeResult(EncoderProbeStatus.TimedOut) with { CleanupCompleted = false });
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Detector.SelectAsync(HardwareEncoderType.Auto));
        Assert.Equal(1, scope.Runner.HardwareCalls);
        scope.Runner.HardwareResponse = _ => Task.FromResult(ProbeResult(EncoderProbeStatus.Available));
        Assert.Equal(HardwareEncoderType.NvidiaNvenc, (await scope.Detector.SelectAsync(HardwareEncoderType.Auto)).Encoder);
        Assert.Equal(2, scope.Runner.HardwareCalls);
    }

    [Fact]
    public async Task Diagnostics_SeparateFailureKindsAndBoundText()
    {
        using var scope = new DetectorScope();
        var sink = new ProbeLogSink();
        var previous = Log.Logger;
        using var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        Log.Logger = logger;
        try
        {
            scope.Runner.HardwareResponse = _ => Task.FromResult(ProbeResult(EncoderProbeStatus.TimedOut) with
                { StderrTail = new string('x', 9000) + "\u001b\nend" });
            await scope.Detector.SelectAsync(HardwareEncoderType.IntelQsv);
            var probe = Assert.Single(sink.Events, x => x.Properties.ContainsKey("ProbeStatus"));
            Assert.Equal("TimedOut", ((ScalarValue)probe.Properties["ProbeStatus"]).Value?.ToString());
            var diagnostic = (string)((ScalarValue)probe.Properties["StderrTail"]).Value!;
            Assert.InRange(diagnostic.Length, 1, 4096);
            Assert.DoesNotContain(diagnostic, char.IsControl);
            Assert.Contains("end", diagnostic);
            Assert.Contains(sink.Events, x => x.Properties.ContainsKey("FallbackReason"));
        }
        finally { Log.Logger = previous; }
    }

    private static EncoderProbeResult ProbeResult(EncoderProbeStatus status) =>
        new(status, status == EncoderProbeStatus.Available ? 0 : 1, TimeSpan.FromMilliseconds(25), "", "", true);

    private sealed class ProbeClock : TimeProvider
    {
        private TimeSpan _elapsed;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + _elapsed;
        public override long GetTimestamp() => _elapsed.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan duration) => _elapsed += duration;
    }

    private sealed class FakeProbeRunner : IEncoderProbeRunner
    {
        public readonly List<string> Calls = new();
        public int HardwareCalls => Calls.Count(x => !x.Contains("-version"));
        public string? AvailableCodec;
        public string Version = "ffmpeg version test-1";
        public Func<CancellationToken, Task<EncoderProbeResult>>? HardwareResponse;
        public int MaxConcurrentCalls;
        private int _active;
        public async Task<EncoderProbeResult> RunAsync(ProcessStartInfo info, TimeSpan timeout, CancellationToken token = default)
        {
            var args = info.Arguments + " " + string.Join(" ", info.ArgumentList);
            Calls.Add(args);
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, ++_active);
            try
            {
                if (args.Contains("-version")) return ProbeResult(EncoderProbeStatus.Available) with { StdoutTail = Version };
                if (HardwareResponse != null) return await HardwareResponse(token);
                return ProbeResult(AvailableCodec != null && args.Contains(AvailableCodec)
                    ? EncoderProbeStatus.Available : EncoderProbeStatus.ProbeFailed);
            }
            finally { _active--; }
        }
    }

    private sealed class DetectorScope : IDisposable
    {
        public readonly string Executable = Path.GetTempFileName();
        public readonly FakeProbeRunner Runner = new();
        public readonly ProbeClock Clock = new();
        public readonly FFmpegEncoderDetector Detector;
        public DetectorScope(bool mac = false)
        {
            IFFmpegPlatformProvider provider = mac
                ? new ScreenRecorder.Platform.macOS.MacOsFFmpegProvider()
                : new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider();
            Detector = new(provider, Executable, Runner, Clock);
        }
        public void Dispose() => File.Delete(Executable);
    }

    private sealed class ProbeLogSink : ILogEventSink
    {
        public readonly List<LogEvent> Events = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Fact]
    public async Task DetectAvailableEncoders_ShouldReturnAutoAndCpuAtMinimum()
    {
        var detector = new FFmpegEncoderDetector(new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider());
        var capabilities = await detector.DetectAvailableEncodersAsync();

        Assert.NotNull(capabilities);
        Assert.True(capabilities.Count >= 2);

        var autoCap = capabilities.FirstOrDefault(c => c.Type == HardwareEncoderType.Auto);
        Assert.NotNull(autoCap);
        Assert.True(autoCap.IsAvailable);

        var cpuCap = capabilities.FirstOrDefault(c => c.Type == HardwareEncoderType.SoftwareCpu);
        Assert.NotNull(cpuCap);
        Assert.True(cpuCap.IsAvailable);
    }

    [Fact]
    public async Task ResolveOptimalEncoder_WhenPreferredIsSoftwareCpu_ShouldReturnSoftwareCpu()
    {
        var detector = new FFmpegEncoderDetector(new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider());
        var resolved = await detector.ResolveOptimalEncoderAsync(HardwareEncoderType.SoftwareCpu);

        Assert.Equal(HardwareEncoderType.SoftwareCpu, resolved);
    }

    [Fact]
    public async Task ResolveOptimalEncoder_WhenAuto_ShouldReturnValidEncoder()
    {
        var detector = new FFmpegEncoderDetector(new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider());
        var resolved = await detector.ResolveOptimalEncoderAsync(HardwareEncoderType.Auto);

        Assert.True(Enum.IsDefined(typeof(HardwareEncoderType), resolved));
        Assert.NotEqual(HardwareEncoderType.Auto, resolved);
    }

    [Theory]
    [InlineData(HardwareEncoderType.SoftwareCpu, "libx264")]
    [InlineData(HardwareEncoderType.NvidiaNvenc, "h264_nvenc")]
    [InlineData(HardwareEncoderType.IntelQsv, "h264_qsv")]
    [InlineData(HardwareEncoderType.AmdAmf, "h264_amf")]
    public void GetVideoCodecArgs_ShouldContainExpectedEncoder(HardwareEncoderType type, string expectedCodecName)
    {
        var args = new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider().BuildOutputArguments(new ScreenRecorder.Core.Models.RecordingConfiguration(), type, "test.mkv");
        Assert.Contains(expectedCodecName, args);
    }

    [Fact]
    public void BuildFFmpegArguments_WithHardwareEncoder_ShouldEmbedEncoderArguments()
    {
        var config = new RecordingConfiguration
        {
            Fps = 30,
            EncoderType = HardwareEncoderType.NvidiaNvenc
        };

        var args = new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider().BuildOutputArguments(
            config, 
            HardwareEncoderType.NvidiaNvenc,
            "test.mkv");

        Assert.Contains("h264_nvenc", args);
    }

    [Theory]
    [InlineData(HardwareEncoderType.SoftwareCpu, "-crf 28")]
    [InlineData(HardwareEncoderType.NvidiaNvenc, "-cq 28")]
    [InlineData(HardwareEncoderType.IntelQsv, "-global_quality 28")]
    [InlineData(HardwareEncoderType.AmdAmf, "-qp_p 28")]
    public void WindowsOutput_UsesSelectedQualityPreset(
        HardwareEncoderType encoderType,
        string expectedArgument)
    {
        var config = new RecordingConfiguration
        {
            VideoQualityPreset = "Compact"
        };

        var args = new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider()
            .BuildOutputArguments(config, encoderType, "test.mkv");

        Assert.Contains(expectedArgument, args);
    }

    [Fact]
    public void WindowsOutput_FlushesShortMatroskaClustersForCrashRecovery()
    {
        var args = new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider()
            .BuildOutputArguments(
                new RecordingConfiguration(),
                HardwareEncoderType.SoftwareCpu,
                "test.mkv");

        Assert.Contains("-flush_packets 1", args);
        Assert.Contains("-cluster_time_limit 1000", args);
    }
}
