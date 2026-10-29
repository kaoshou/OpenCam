// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Platform.Linux;
namespace ScreenRecorder.Media.Tests;
public sealed class LinuxEngineEventTests
{
    [Fact]
    public async Task WrapperRetargetsFailureIdentitySoOrchestratorAcceptsItsOwnEngine()
    {
        var source = new Source(); var wrapper = new LinuxRecordingEngine(source);
        object? errorSender = null, warningSender = null, audioSender = null; var errors = 0;
        wrapper.EngineErrorOccurred += (s,e) => { errorSender = s; errors++; Assert.Equal("source_lost", e); };
        wrapper.EngineWarningOccurred += (s,e) => warningSender = s;
        wrapper.AudioDeviceLost += (s,e) => audioSender = s;
        source.Raise(); Assert.Same(wrapper, errorSender); Assert.Same(wrapper, warningSender); Assert.Same(wrapper, audioSender);
        await wrapper.DisposeAsync(); source.Raise(); Assert.Equal(1, errors);
    }
    private sealed class Source : IScreenRecorderEngine, IBoundRecordingOutput {
        public bool IsRunning => false; public bool UseSyntheticCaptureSource {get;set;}
        public Stream? OutputStream {get;set;} public TimeSpan CurrentRecordedTime => TimeSpan.Zero;
        public long CurrentFramesRecorded => 0; public HardwareEncoderType ActiveEncoder => HardwareEncoderType.SoftwareCpu;
        public CaptureSelection? ActiveCapture => null;
        public event EventHandler<string>? EngineErrorOccurred; public event EventHandler<string>? EngineWarningOccurred; public event EventHandler? AudioDeviceLost;
        public void Raise() { EngineErrorOccurred?.Invoke(this,"source_lost"); EngineWarningOccurred?.Invoke(this,"warning"); AudioDeviceLost?.Invoke(this,EventArgs.Empty); }
        public Task StartRecordingAsync(string p, RecordingConfiguration c, CaptureRegion r, CancellationToken t=default) => Task.CompletedTask;
        public Task StopRecordingAsync(CancellationToken t=default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
