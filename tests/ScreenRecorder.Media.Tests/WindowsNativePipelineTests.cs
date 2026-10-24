// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.IO.Pipes;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Platform.Windows;

namespace ScreenRecorder.Media.Tests;

public sealed class WindowsNativePipelineTests
{
    [Fact]
    public void WindowsOwnedProducerIsExplicitAndRetainsSelectedEncoder()
    {
        INativeVideoPlatformProvider provider = new WindowsFFmpegProvider();
        Assert.True(provider.RequiresEditablePointer);
        Assert.Null(provider.RequiredNativeEncoder);
        Assert.True(provider.UsesNamedPipeVideo);
        var config = new RecordingConfiguration { NativeCaptureEnabled = true, EditablePointerEnabled = true,
            AudioSource = AudioSourceType.None };
        var input = provider.BuildNativeInput(config, "-f matroska -i owned-pipe ", false, null, null);
        Assert.Contains("owned-pipe", input); Assert.DoesNotContain("gdigrab", input);
        var output = provider.BuildNativeOutput(config, HardwareEncoderType.NvidiaNvenc, "capture.mkv");
        Assert.Contains("-c:v h264_nvenc", output); Assert.DoesNotContain("-c:v copy", output);
        Assert.Contains("-fps_mode passthrough", output);
    }

    [Fact]
    public async Task NamedPipeConnectsBeforeBeginAndDrainsBinaryBytesBeforeEof()
    {
        await using var bridge = new NativeVideoPipe();
        await using var client = new NamedPipeClientStream(".", bridge.Name, PipeDirection.In, PipeOptions.Asynchronous);
        var producer = new BufferCapture();
        var start = bridge.StartAsync(producer, default);
        Assert.False(producer.Begun);
        await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await start.WaitAsync(TimeSpan.FromSeconds(3));
        var bytes = new byte[4];
        await client.ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new byte[] { 0, 1, 128, 255 }, bytes);
        Assert.Equal(0, await client.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        await bridge.CompleteAsync();
        Assert.True(producer.Begun);
    }

    [Fact]
    public async Task NamedPipeDisposeCancelsAnUnconnectedProducerWithinBound()
    {
        var bridge = new NativeVideoPipe();
        var producer = new BufferCapture();
        var start = bridge.StartAsync(producer, default);
        await bridge.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.False(producer.Begun);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task OwnedWindowsProducerRequiresBothNativeAndEditableFlags(bool native, bool editable)
    {
        var path = Path.Combine(Path.GetTempPath(), $"native-gate-{Guid.NewGuid():N}.mkv");
        var provider = new SyntheticProvider();
        await using var engine = new FFmpegScreenRecorderEngine(provider, pinnedEncoder: HardwareEncoderType.SoftwareCpu);
        try
        {
            await engine.StartRecordingAsync(path, new() { NativeCaptureEnabled = native, EditablePointerEnabled = editable,
                AudioSource = AudioSourceType.None, EncoderType = HardwareEncoderType.SoftwareCpu, Fps = 30 }, new(0, 0, 320, 240));
            Assert.Null(provider.Capture);
            await engine.StopRecordingAsync();
        }
        finally { await engine.StopRecordingAsync(); File.Delete(path); }
    }

    [MacOsOnlyFact]
    public Task SyntheticOwnedProducerForwardsPrivateMetadataUntilFinalStopOnMac() => VerifyEngineAsync();

    [WindowsOnlyFact]
    public Task SyntheticOwnedProducerForwardsPrivateMetadataUntilFinalStopOnWindows() => VerifyEngineAsync();

    private static async Task VerifyEngineAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"windows-owned-pipe-{Guid.NewGuid():N}.mkv");
        var provider = new SyntheticProvider();
        await using var engine = new FFmpegScreenRecorderEngine(provider, pinnedEncoder: HardwareEncoderType.SoftwareCpu);
        var events = new List<NativePointerEvent>(); var incomplete = new List<string>();
        ((INativePointerCapture)engine).PointerMetadata += (_, data) => events.Add(data);
        ((INativePointerCapture)engine).PointerIncomplete += (_, reason) => incomplete.Add(reason);
        try
        {
            await engine.StartRecordingAsync(path, new() { NativeCaptureEnabled = true, EditablePointerEnabled = true,
                AudioSource = AudioSourceType.None, EncoderType = HardwareEncoderType.SoftwareCpu, Fps = 30 }, new(0, 0, 320, 240));
            Assert.Equal(HardwareEncoderType.SoftwareCpu, engine.ActiveEncoder);
            Assert.Equal(CaptureBackend.WindowsGraphicsCapture, engine.ActiveCapture!.Backend);
            provider.Capture!.FailPointer();
            Assert.True(engine.IsRunning);
            Assert.Single(incomplete);
            Assert.DoesNotContain("private-coordinate", incomplete[0]);
            await engine.StopRecordingAsync().WaitAsync(TimeSpan.FromSeconds(12));
            Assert.True(provider.Capture.Stopped);
            Assert.Equal(new long[] { 1000, 2000 }, events.Select(e => e.Pointer!.Value.HostMicroseconds));
            var count = events.Count;
            provider.Capture.PublishAfterStop();
            Assert.Equal(count, events.Count); // Final stop was delivered; subscriptions are now gone.
            var media = await new MediaFileProbe().ProbeAsync(path);
            Assert.True(media.IsValid, media.RawJsonOutput);
            Assert.Equal(1, media.VideoStreamCount);
        }
        finally { await engine.StopRecordingAsync(); File.Delete(path); }
    }

    private sealed class BufferCapture : INativeVideoCapture
    {
        public bool Begun { get; private set; }
        public Stream Output { get; } = new MemoryStream(new byte[] { 0, 1, 128, 255 });
        public CaptureSelection Selection { get; } = new(CaptureBackend.WindowsGraphicsCapture, CaptureFallbackReason.None,
            "test", null, null, new(0, 0, 32, 32));
        public event EventHandler<string>? CaptureFailed { add { } remove { } }
        public Task BeginAsync(CancellationToken token) { Begun = true; return Task.CompletedTask; }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() { Output.Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class SyntheticProvider : WindowsFFmpegProvider, INativeVideoPlatformProvider, IFFmpegPlatformProvider
    {
        public SyntheticCapture? Capture { get; private set; }
        bool INativeVideoPlatformProvider.UsesNamedPipeVideo => OperatingSystem.IsWindows();
        string IFFmpegPlatformProvider.BuildInputArguments(RecordingConfiguration config, int x, int y, int width, int height,
            bool synthetic, bool directMic, string? system, string? microphone) =>
            "-re -f lavfi -i testsrc2=size=320x240:rate=30 ";
        Task<INativeVideoCapture> INativeVideoPlatformProvider.PrepareNativeVideoAsync(RecordingConfiguration config,
            CaptureRegion bounds, CaptureSelection? pinned, CancellationToken token)
        {
            Capture = new(bounds);
            return Task.FromResult<INativeVideoCapture>(Capture);
        }
    }

    private sealed class SyntheticCapture : INativeVideoCapture, INativePointerCapture
    {
        private readonly Process _process;
        private readonly Task<string> _stderr;
        private Task? _stop;
        public SyntheticCapture(CaptureRegion bounds)
        {
            Selection = new(CaptureBackend.WindowsGraphicsCapture, CaptureFallbackReason.None, "synthetic", null, null, bounds);
            _process = Process.Start(new ProcessStartInfo(FFmpegDiscovery.FindFFmpegExecutable()!) {
                Arguments = "-v error -re -f lavfi -i testsrc2=size=320x240:rate=30 -c:v libx264 -preset ultrafast -tune zerolatency -bf 0 -flush_packets 1 -cluster_time_limit 100 -f matroska pipe:1",
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            _stderr = _process.StandardError.ReadToEndAsync();
        }
        public CaptureSelection Selection { get; }
        public Stream Output => _process.StandardOutput.BaseStream;
        public bool Stopped { get; private set; }
        public event EventHandler<string>? CaptureFailed { add { } remove { } }
        public event EventHandler<NativePointerEvent>? PointerMetadata;
        public event EventHandler<string>? PointerIncomplete;
        public Task BeginAsync(CancellationToken token)
        {
            PointerMetadata?.Invoke(this, new(new PointerSample(1000, 1, 2, true, 0), null));
            return Task.CompletedTask;
        }
        public void FailPointer() => PointerIncomplete?.Invoke(this, "private-coordinate");
        public void PublishAfterStop() => PointerMetadata?.Invoke(this, new(new PointerSample(3000, 1, 2, true, 0), null));
        public Task StopAsync() => _stop ??= StopCoreAsync();
        private async Task StopCoreAsync()
        {
            if (!_process.HasExited) {
                await _process.StandardInput.WriteLineAsync("q"); await _process.StandardInput.FlushAsync();
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            }
            await _stderr;
            PointerMetadata?.Invoke(this, new(new PointerSample(2000, 1, 2, true, 0), null));
            Stopped = true;
        }
        public async ValueTask DisposeAsync() { try { await StopAsync(); } finally { _process.Dispose(); } }
    }
}
