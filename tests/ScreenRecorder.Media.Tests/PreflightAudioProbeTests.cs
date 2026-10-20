using System.Text;
using ScreenRecorder.Media.Preflight;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public class PreflightAudioProbeTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"codec_type":"video","codec_name":"h264"}]""")]
    [InlineData("""[{"codec_type":"audio","codec_name":"pcm_s16le"},{"codec_type":"video","codec_name":"h264"}]""")]
    [InlineData("""[{"codec_type":"audio","codec_name":"aac"}]""")]
    public async Task RejectsMissingAudioVideoAndUnexpectedCodec(string streams)
    {
        await using var storage = new PreflightTrialStorage();
        await storage.Writer.WriteAsync(new byte[16]);
        await storage.SealAsync(default);
        await using var input = storage.OpenRead();
        var process = new Process(streams);
        var probe = new PreflightAudioProbe(() => process, () => process);
        await Assert.ThrowsAsync<InvalidDataException>(() => probe.ValidateAsync(input, default));
        Assert.False(process.Decoded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(384000)]
    [InlineData(1920001)]
    public async Task RejectsEmptyShortOrPartialDecodedFrames(int bytes)
    {
        await using var storage = new PreflightTrialStorage();
        await storage.Writer.WriteAsync(new byte[16]);
        await storage.SealAsync(default);
        await using var input = storage.OpenRead();
        var process = new Process("""[{"codec_type":"audio","codec_name":"pcm_s16le"}]""", bytes);
        var probe = new PreflightAudioProbe(() => process, () => process);
        await Assert.ThrowsAsync<InvalidDataException>(() => probe.ValidateAsync(input, default));
        Assert.True(process.Decoded);
    }

    private sealed class Process(string streams, int bytes = 0) : IProjectMediaProcess, IProjectStreamingMediaProcess
    {
        public bool Decoded;
        public Task<ProjectMediaResult> RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> inputs,
            FileStream? output, CancellationToken ct) =>
            Task.FromResult(new ProjectMediaResult(Encoding.UTF8.GetBytes($$"""{"streams":{{streams}}}"""), ""));
        public async Task RunStreamingAsync(ProjectMediaJob job, FileStream input,
            Func<Stream, CancellationToken, Task> consume, CancellationToken ct)
        {
            Decoded = true;
            using var pcm = new MemoryStream(new byte[bytes]);
            await consume(pcm, ct);
        }
    }
}
