// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Text.Json;
using System.Collections.Concurrent;

namespace ScreenRecorder.Media.Tests;

public sealed class ObsScreenRecorderEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opencam-obs-engine-" + Guid.NewGuid().ToString("N"));
    private static readonly MonitorInfo Monitor = new(2, "native-display-id", new(0, 0, 640, 480), true, 1);
    private const string Ready = "printf '%s\\n' 'OCOBS {\"event\":\"ready\",\"protocol\":1,\"backend\":\"synthetic\",\"encoder\":\"cpu\"}' >&2\n";
    private const string Progress = "printf '%s\\n' 'OCOBS {\"event\":\"progress\",\"frames\":30,\"mediaUs\":1000000,\"bytes\":10,\"systemPeak\":0.25,\"microphonePeak\":0.75}' >&2\n";
    private const string Stop = "while IFS= read -r line; do\n[ \"$line\" = STOP ] && break\ndone\nprintf '%s\\n' 'OCOBS {\"event\":\"stopped\"}' >&2\n";

    public ObsScreenRecorderEngineTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task NativeLaunchPinsArgumentAndLoaderSearchPathsToOwnedRuntime()
    {
        var keys = new[] { "OBS_PLUGINS_PATH", "OBS_PLUGINS_DATA_PATH", "DYLD_INSERT_LIBRARIES", "DYLD_LIBRARY_PATH",
            "DYLD_FRAMEWORK_PATH", "DYLD_FALLBACK_LIBRARY_PATH", "LD_PRELOAD", "LD_LIBRARY_PATH" };
        var before = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var key in keys) Environment.SetEnvironmentVariable(key, "unverified-user-library");
            await using var engine = new ObsScreenRecorderEngine(Path.Combine(_root, "helper"), _root, (_, _, _) => Monitor);
            var start = engine.CreateProcessStartInfo();
            Assert.Equal(new[] { "--runtime", _root }, start.ArgumentList);
            Assert.False(start.UseShellExecute);
            Assert.All(keys, key => Assert.False(start.Environment.ContainsKey(key)));
            if (OperatingSystem.IsWindows())
                Assert.Equal(Path.Combine(_root, "bin", "64bit") + Path.PathSeparator + Environment.SystemDirectory, start.Environment["PATH"]);
        }
        finally
        {
            foreach (var key in keys) Environment.SetEnvironmentVariable(key, before[key]);
        }
    }

    [UnixOnlyFact]
    public async Task CopiesBinaryOutputIntoCallerOwnedStreamAndStopsWithoutReopeningOrDisposingIt()
    {
        if (OperatingSystem.IsWindows()) return;
        var existing = Path.Combine(_root, "already-owned.mkv");
        await File.WriteAllTextAsync(existing, "original");
        using var destination = new MemoryStream();
        await using var engine = Engine("printf 'MKV-prefix'\n" + Ready + Progress + Stop);
        engine.OutputStream = destination;
        await engine.StartRecordingAsync(existing, Config(), Monitor.Bounds);
        await WaitUntilAsync(() => engine.CurrentFramesRecorded == 30);
        Assert.True(engine.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(1), engine.CurrentRecordedTime);
        Assert.Equal(0.25, engine.ReadInputLevels(DateTimeOffset.UtcNow).SystemAudio?.Peak);
        Assert.Equal(0.75, engine.ReadInputLevels(DateTimeOffset.UtcNow).Microphone?.Peak);
        Assert.Equal("libobs-v1", engine.ActiveCapture?.RecordingCore);
        await engine.StopRecordingAsync();
        Assert.False(engine.IsRunning);
        Assert.Equal("MKV-prefix", Encoding.ASCII.GetString(destination.ToArray()));
        Assert.True(destination.CanWrite);
        Assert.Equal("original", await File.ReadAllTextAsync(existing));
        Assert.Null(engine.ReadInputLevels(DateTimeOffset.UtcNow).SystemAudio);
        var request = await File.ReadAllTextAsync(Path.Combine(_root, "helper.request"));
        Assert.Contains("\"microphone\":\"selected-microphone\"", request);
        Assert.DoesNotContain(existing, request);
    }

    [ObsUnixOnlyTheory]
    [InlineData("exit 0\n")]
    [InlineData("printf 'MKV-prefix'\nexit 7\n")]
    [InlineData("printf 'MKV-prefix'\nprintf '%s\\n' 'OCOBS {bad-json private-path}' >&2\n")]
    [InlineData("printf 'MKV-prefix'\nprintf '%s\\n' 'OCOBS {\"event\":\"ready\",\"protocol\":2,\"backend\":\"synthetic\",\"encoder\":\"cpu\"}' >&2\n")]
    [InlineData("printf 'MKV-prefix'\nprintf '%s\\n' 'OCOBS {\"event\":\"ready\",\"protocol\":1,\"backend\":\"synthetic\",\"encoder\":\"cpu\",\"monitor\":\"wrong-source\"}' >&2\n")]
    public async Task RejectsEofExitAndMalformedProtocolDuringStartupPreservingOutput(string body)
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new MemoryStream();
        await using var engine = Engine(body);
        engine.OutputStream = output;
        var errors = new List<string>();
        engine.EngineErrorOccurred += (_, value) => errors.Add(value);
        var error = await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds));
        Assert.False(engine.IsRunning);
        Assert.DoesNotContain("private-path", error.Message);
        Assert.DoesNotContain(errors, value => value.Contains("private-path"));
        if (body.Contains("MKV-prefix")) Assert.Equal("MKV-prefix", Encoding.ASCII.GetString(output.ToArray()));
    }

    [UnixOnlyFact]
    public async Task DetectsStdoutEofWhileHelperRemainsAlive()
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new MemoryStream();
        await using var engine = Engine("printf 'MKV-prefix'\n" + Ready + "exec 1>&-\nwhile IFS= read -r line; do :; done\n");
        engine.OutputStream = output;
        var error = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.EngineErrorOccurred += (_, value) => error.TrySetResult(value);
        try { await engine.StartRecordingAsync("unused", Config(), Monitor.Bounds); } catch (IOException) { }
        await error.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(engine.IsRunning);
        Assert.Equal("MKV-prefix", Encoding.ASCII.GetString(output.ToArray()));
    }

    [UnixOnlyFact]
    public async Task FailsClosedOnDestinationWriteFailure()
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new FailingDestination();
        await using var engine = Engine("printf 'MKV-prefix'\n" + Ready + Stop);
        engine.OutputStream = output;
        await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds));
        Assert.False(engine.IsRunning);
    }

    [UnixOnlyFact]
    public async Task CancelsStartupAndBoundsUnresponsiveStop()
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new MemoryStream();
        await using (var engine = Engine("while IFS= read -r line; do :; done\n"))
        {
            engine.OutputStream = output;
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds, cancelled.Token));
            Assert.False(engine.IsRunning);
        }
        await using (var engine = Engine("printf 'MKV-prefix'\n" + Ready + "while :; do :; done\n", TimeSpan.FromMilliseconds(200)))
        {
            engine.OutputStream = output;
            await engine.StartRecordingAsync("unused", Config(), Monitor.Bounds);
            await Assert.ThrowsAsync<IOException>(() => engine.StopRecordingAsync().WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(engine.IsRunning);
        }
    }

    [UnixOnlyFact]
    public async Task NeverOverwritesAnExistingUnboundOutput()
    {
        if (OperatingSystem.IsWindows()) return;
        var output = Path.Combine(_root, "existing.mkv");
        await File.WriteAllTextAsync(output, "original");
        await using var engine = Engine("printf 'MKV-prefix'\n" + Ready + Stop);
        await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync(output, Config(), Monitor.Bounds));
        Assert.Equal("original", await File.ReadAllTextAsync(output));
        Assert.False(File.Exists(Path.Combine(_root, "helper.request")));
    }

    [UnixOnlyFact]
    public async Task ReadyWithoutAnyMuxedBytesDoesNotConfirmRecording()
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new MemoryStream();
        await using var engine = Engine(Ready + "while IFS= read -r line; do :; done\n");
        engine.OutputStream = output;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds, cancel.Token));
        Assert.False(engine.IsRunning);
        Assert.Empty(output.ToArray());
    }

    [UnixOnlyFact]
    public async Task InvalidProgressAfterStartupSignalsFailureAndRetainsCopiedBytes()
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new MemoryStream();
        await using var engine = Engine("printf 'MKV-prefix'\n" + Ready +
            "IFS= read -r line\nprintf '%s\\n' 'OCOBS {\"event\":\"progress\",\"frames\":-1,\"mediaUs\":1000,\"bytes\":10,\"systemPeak\":0,\"microphonePeak\":0}' >&2\n");
        engine.OutputStream = output;
        await engine.StartRecordingAsync("unused", Config(), Monitor.Bounds);
        await Assert.ThrowsAsync<IOException>(() => engine.StopRecordingAsync());
        Assert.False(engine.IsRunning);
        Assert.Equal("MKV-prefix", Encoding.ASCII.GetString(output.ToArray()));
    }

    [UnixOnlyFact]
    public async Task NonzeroExitAfterStopCannotBeReportedAsSuccessfulFinalization()
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new MemoryStream();
        await using var engine = Engine("printf 'MKV-prefix'\n" + Ready + Stop + "exit 7\n");
        engine.OutputStream = output;
        await engine.StartRecordingAsync("unused", Config(), Monitor.Bounds);
        await Assert.ThrowsAsync<IOException>(() => engine.StopRecordingAsync());
        Assert.Equal("MKV-prefix", Encoding.ASCII.GetString(output.ToArray()));
    }

    [UnixOnlyFact]
    public async Task ProgressCannotRefreshAnAudioCallbackThatIsAlreadyStale()
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new MemoryStream();
        var progress = Progress.Replace("\"systemPeak\":0.25", "\"systemAgeUs\":3000000,\"systemPeak\":0.25")
            .Replace("\"microphonePeak\":0.75", "\"microphoneAgeUs\":250000,\"microphonePeak\":0.75");
        await using var engine = Engine("printf 'MKV-prefix'\n" + Ready + progress + Stop);
        engine.OutputStream = output;
        await engine.StartRecordingAsync("unused", Config(), Monitor.Bounds);
        await WaitUntilAsync(() => engine.CurrentFramesRecorded == 30);
        var now = DateTimeOffset.UtcNow;
        var levels = engine.ReadInputLevels(now);
        Assert.Null(levels.SystemAudio);
        Assert.Equal(0.75, levels.Microphone?.Peak);
        Assert.True(now - levels.Microphone!.Value.CapturedAt >= TimeSpan.FromMilliseconds(200));
    }

    [UnixOnlyFact]
    public async Task AudioStallWarningKeepsVideoAndTheOtherSourceRunning()
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new MemoryStream();
        await using var engine = Engine("printf 'MKV-prefix'\n" + Ready +
            "printf '%s\\n' 'OCOBS {\"event\":\"warning\",\"code\":\"audio_source_stalled\",\"source\":\"system\"}' >&2\n" + Progress + Stop);
        engine.OutputStream = output;
        var warning = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lost = false;
        engine.EngineWarningOccurred += (_, value) => warning.TrySetResult(value);
        engine.AudioDeviceLost += (_, _) => lost = true;
        await engine.StartRecordingAsync("unused", Config(), Monitor.Bounds);
        await warning.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(engine.IsRunning);
        Assert.False(lost);
        await engine.StopRecordingAsync();
    }

    [UnixOnlyFact]
    public async Task RejectsInvalidAudioAgeInsteadOfShowingInventedFreshLevels()
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new MemoryStream();
        var progress = Progress.Replace("\"systemPeak\":0.25", "\"systemAgeUs\":-2,\"systemPeak\":0.25");
        await using var engine = Engine("printf 'MKV-prefix'\n" + Ready + progress + Stop);
        engine.OutputStream = output;
        var error = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.EngineErrorOccurred += (_, value) => error.TrySetResult(value);
        try { await engine.StartRecordingAsync("unused", Config(), Monitor.Bounds); } catch (IOException) { }
        await error.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(engine.IsRunning);
    }

    [UnixOnlyFact]
    public async Task DuplicateStartCannotTerminateAnExistingRecording()
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new MemoryStream();
        await using var engine = Engine("printf 'MKV-prefix'\n" + Ready + Stop);
        engine.OutputStream = output;
        await engine.StartRecordingAsync("unused", Config(), Monitor.Bounds);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds));
        Assert.True(engine.IsRunning);
        await engine.StopRecordingAsync();
    }

    [ObsUnixOnlyTheory]
    [InlineData("display_identity", true)]
    [InlineData("audio_device_identity", true)]
    [InlineData("capture_source", true)]
    [InlineData("capture_start_timeout", true)]
    [InlineData("graphics_startup", true)]
    [InlineData("runtime_module", true)]
    [InlineData("encoder_unavailable", false)]
    [InlineData("encoder_start", false)]
    public async Task ClassifiesOnlyExplicitSetupFailureBeforeAnyOutputForInitialFallback(string code, bool capture)
    {
        if (OperatingSystem.IsWindows()) return;
        await using var engine = Engine("printf '%s\\n' 'OCOBS {\"event\":\"error\",\"code\":\"" + code + "\"}' >&2\nexit 2\n");
        using var output = new MemoryStream();
        engine.OutputStream = output;
        if (capture)
        {
            var error = await Assert.ThrowsAsync<CaptureStartupException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds));
            Assert.Equal(OperatingSystem.IsWindows() ? CaptureBackend.Gdi : CaptureBackend.PlatformDefault, error.CompatibilityBackend);
        }
        else await Assert.ThrowsAsync<EncoderStartupException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds));
    }

    [UnixOnlyFact]
    public async Task EvenOneCopiedByteDisallowsStartupFallback()
    {
        if (OperatingSystem.IsWindows()) return;
        using var output = new MemoryStream();
        await using var engine = Engine("printf 'x'\nprintf '%s\\n' 'OCOBS {\"event\":\"error\",\"code\":\"capture_source\"}' >&2\nexit 2\n");
        engine.OutputStream = output;
        await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds));
        Assert.Equal("x", Encoding.ASCII.GetString(output.ToArray()));
    }

    [UnixOnlyFact]
    public async Task StrictNativeStartupFailureCannotRequestCompatibilityFallback()
    {
        await using var engine = Engine("printf '%s\\n' 'OCOBS {\"event\":\"error\",\"code\":\"capture_start_timeout\"}' >&2\nexit 2\n",
            allowCompatibilityFallback: false);
        using var output = new MemoryStream();
        engine.OutputStream = output;
        await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds));
        Assert.False(engine.IsRunning);
        Assert.Equal(0, output.Length);
    }

    [UnixOnlyFact]
    public async Task StrictStartupReportsSafeReasonAndSourceReadinessWithoutLeakingHelperText()
    {
        var sink = new PerformanceSink();
        var previous = Log.Logger;
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        Log.Logger = logger;
        try
        {
            const string details = "{\"stage\":\"source_wait\",\"elapsedMs\":10001,\"sourceWidth\":640,\"sourceHeight\":480,\"expectedWidth\":640,\"expectedHeight\":480,\"systemSelected\":true,\"microphoneSelected\":true,\"systemSamples\":true,\"microphoneSamples\":false,\"frames\":0,\"bytes\":0,\"private\":\"private-device-path\"}";
            await using var engine = Engine("printf '%s\\n' 'OCOBS {\"event\":\"error\",\"code\":\"capture_start_timeout\",\"startup\":" + details + "}' >&2\nexit 2\n", allowCompatibilityFallback: false);
            using var output = new MemoryStream();
            engine.OutputStream = output;
            var error = await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds));
            Assert.Contains("capture_start_timeout", error.Message);
            Assert.Contains("source_wait", error.Message);
            var log = string.Join("\n", sink.Events.Select(e => e.RenderMessage()));
            Assert.Contains("MicrophoneSamples", log);
            Assert.DoesNotContain("private-device-path", log + error);
            Assert.False(engine.IsRunning);
            Assert.Equal(0, output.Length);
        }
        finally { Log.Logger = previous; }
    }

    [UnixOnlyFact]
    public async Task UnknownNativeErrorCannotExposeArbitraryDeviceText()
    {
        await using var engine = Engine("printf '%s\\n' 'OCOBS {\"event\":\"error\",\"code\":\"private-device-path\"}' >&2\nexit 2\n", allowCompatibilityFallback: false);
        var errors = new List<string>();
        engine.EngineErrorOccurred += (_, value) => errors.Add(value);
        using var output = new MemoryStream(); engine.OutputStream = output;
        var error = await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds));
        Assert.Contains("native_failure", error.Message);
        Assert.DoesNotContain("private-device-path", error.ToString() + string.Join("\n", errors));
    }

    [ObsUnixOnlyTheory]
    [InlineData("\"frames\":0")]
    [InlineData("\"microphoneSamples\":false")]
    [InlineData("\"sourceWidth\":0")]
    [InlineData("\"elapsedMs\":-1")]
    [InlineData("\"stage\":\"private-device-path\"")]
    public async Task ReadyDiagnosticsCannotContradictRealSourceAndFrameReadiness(string invalid)
    {
        var fields = new Dictionary<string,object> { ["stage"]="first_frame",["elapsedMs"]=20,
            ["sourceWidth"]=640,["sourceHeight"]=480,["expectedWidth"]=640,["expectedHeight"]=480,
            ["systemSelected"]=true,["microphoneSelected"]=true,["systemSamples"]=true,["microphoneSamples"]=true,
            ["frames"]=1,["bytes"]=10 };
        using var replacement = JsonDocument.Parse("{" + invalid + "}");
        var property = replacement.RootElement.EnumerateObject().Single();
        fields[property.Name] = property.Value.Clone();
        var ready = Ready.Replace("\"encoder\":\"cpu\"}","\"encoder\":\"cpu\",\"startup\":" + JsonSerializer.Serialize(fields) + "}");
        await using var engine = Engine("printf 'MKV-prefix'\n" + ready + Stop);
        using var output = new MemoryStream();engine.OutputStream = output;
        await Assert.ThrowsAsync<IOException>(() => engine.StartRecordingAsync("unused", Config(), Monitor.Bounds));
        Assert.False(engine.IsRunning);
        Assert.Equal("MKV-prefix",Encoding.ASCII.GetString(output.ToArray()));
    }

    [MacOsOnlyFact]
    public async Task RetinaSourceReadinessUsesBackingPixelsWithoutChangingOutputBounds()
    {
        const string details = "{\"stage\":\"first_frame\",\"elapsedMs\":20,\"sourceWidth\":1280,\"sourceHeight\":960,\"expectedWidth\":1280,\"expectedHeight\":960,\"systemSelected\":true,\"microphoneSelected\":true,\"systemSamples\":true,\"microphoneSamples\":true,\"frames\":1,\"bytes\":10}";
        var ready = Ready.Replace("synthetic", "sck").Replace("\"encoder\":\"cpu\"}", "\"encoder\":\"cpu\",\"startup\":" + details + "}");
        await using var engine = Engine("printf 'MKV-prefix'\n" + ready + Stop, synthetic: false);
        using var output = new MemoryStream(); engine.OutputStream = output;
        await engine.StartRecordingAsync("unused", Config(), Monitor.Bounds);
        Assert.True(engine.IsRunning);
        Assert.Equal(640, engine.ActiveCapture!.Bounds.Width);
        await engine.StopRecordingAsync();
    }

    [UnixOnlyFact]
    public async Task NativePerformanceLogsOwnedWriteMetricsWithoutPrivatePathsOrInventedFfmpegCounters()
    {
        var sink = new PerformanceSink();
        var previousLogger = Log.Logger;
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        Log.Logger = logger;
        try
        {
            using var output = new MemoryStream();
            await using var engine = Engine("printf 'MKV-prefix'\n" + Ready + Progress.Replace("\"bytes\":10", "\"bytes\":999999") + Stop);
            engine.OutputStream = output;
            await engine.StartRecordingAsync("unused", Config(), Monitor.Bounds);
            await WaitUntilAsync(() => engine.CurrentFramesRecorded == 30);
            await engine.StopRecordingAsync();
            var measurements = sink.Events.Select(item => item.Properties.TryGetValue("Measurement", out var property)
                ? (property as ScalarValue)?.Value as string : null).Where(value => value is not null).ToArray();
            string? stopped = null;
            foreach (var measurement in measurements)
            {
                using var entry = JsonDocument.Parse(measurement!);
                if (entry.RootElement.GetProperty("Phase").GetString() != "Stopped") continue;
                Assert.Null(stopped);
                stopped = measurement;
            }
            Assert.NotNull(stopped);
            using var json = JsonDocument.Parse(stopped);
            var metrics = json.RootElement.GetProperty("Metrics");
            Assert.Equal(30, metrics.GetProperty("Frames").GetInt64());
            Assert.Equal(1, metrics.GetProperty("MediaSeconds").GetDouble());
            Assert.Equal(10, metrics.GetProperty("OutputBytes").GetInt64());
            Assert.True(metrics.GetProperty("WriteWaitMs").GetDouble() >= 0);
            Assert.Equal(JsonValueKind.Null, metrics.GetProperty("FfmpegFps").ValueKind);
            Assert.Equal(JsonValueKind.Null, metrics.GetProperty("Speed").ValueKind);
            Assert.Equal(JsonValueKind.Null, metrics.GetProperty("DuplicatedFrames").ValueKind);
            Assert.Equal(JsonValueKind.Null, metrics.GetProperty("DroppedFrames").ValueKind);
            Assert.DoesNotContain(_root, stopped);
            Assert.DoesNotContain("selected-microphone", stopped);
            Assert.DoesNotContain("native-display-id", stopped);
        }
        finally { Log.Logger = previousLogger; }
    }

    private sealed class PerformanceSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent item) => Events.Enqueue(item);
    }

    private ObsScreenRecorderEngine Engine(string body, TimeSpan? stopTimeout = null, bool allowCompatibilityFallback = true, bool synthetic = true)
    {
        var path = Path.Combine(_root, "helper");
        File.WriteAllText(path, "#!/bin/sh\nIFS= read -r request\nprintf '%s' \"$request\" > \"$0.request\"\n" + body);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new ObsScreenRecorderEngine(path, _root, (_, _, _) => Monitor,
            pinnedEncoder: HardwareEncoderType.SoftwareCpu, startupTimeout: TimeSpan.FromSeconds(2), stopTimeout: stopTimeout,
            allowCompatibilityFallback: allowCompatibilityFallback)
            { UseSyntheticCaptureSource = synthetic };
    }

    private static RecordingConfiguration Config() => new() { Fps = 30,
        AudioSource = AudioSourceType.SystemAndMicrophone, MicrophoneDeviceId = "selected-microphone" };

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate()) await Task.Delay(10, deadline.Token);
    }

    private sealed class FailingDestination : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("destination failed"));
    }

    public void Dispose() => Directory.Delete(_root, true);
}

internal sealed class ObsUnixOnlyTheoryAttribute : TheoryAttribute
{
    public ObsUnixOnlyTheoryAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Requires a Unix subprocess fixture; physical Windows capture remains separate acceptance.";
    }
}
