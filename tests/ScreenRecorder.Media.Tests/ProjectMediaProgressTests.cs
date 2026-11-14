// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectMediaProgressTests
{
    [Fact]
    public async Task SplitProtocolLinesReportMonotonicFramesAndRetainRealErrors()
    {
        var values=new List<double>();
        var job=ProjectMediaJob.MuxCopiedVideoAndAudio(1024).WithProgress(new Immediate(values.Add),totalFrames:100);
        using var stream=new SplitStream("frame=25\r\nfps=30\nstream_0_0_q=18.0\nprogress=continue\nframe=N/A\nframe=-2\nframe=50\nframe=10\nframe=9223372036854775808\n解碼錯誤\nframe=1000\nprogress=end");
        var error=await job.ReadDiagnosticAsync(stream,128);
        Assert.Equal(new[]{.25,.5,1},values);
        Assert.Equal("解碼錯誤\n",Encoding.UTF8.GetString(error));
    }
    [Fact]
    public async Task AudioUsesCompletedSampleTimeAndDoesNotInventCompletionOnEnd()
    {
        var values=new List<double>();
        var job=ProjectMediaJob.MuxCopiedVideoAndAudio(1024).WithProgress(new Immediate(values.Add),totalAudioSamples:96000);
        using var stream=new SplitStream("frame=99999\nout_time_us=N/A\nout_time_us=-200\nout_time_us=500000\nout_time_us=1000000\nprogress=end\n");
        await job.ReadDiagnosticAsync(stream,128);
        Assert.Equal(new[]{.25,.5},values);
    }
    [Fact]
    public async Task LongProgressDoesNotConsumeDiagnosticQuotaButErrorsRemainBounded()
    {
        var job=ProjectMediaJob.MuxCopiedVideoAndAudio(1024).WithProgress(new Immediate(_=>{}),totalFrames:100);
        var text=string.Concat(Enumerable.Repeat("frame=10\nprogress=continue\n",1000));
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(text+"real error\n")))
            Assert.Equal("real error\n",Encoding.UTF8.GetString(await job.ReadDiagnosticAsync(stream,32)));
        using(var oversized=new MemoryStream(Encoding.UTF8.GetBytes(new string('x',33))))
            await Assert.ThrowsAsync<InvalidDataException>(()=>job.ReadDiagnosticAsync(oversized,32));
    }
    [Fact]
    public async Task UntrackedJobsKeepTheirOriginalDiagnosticsAndLimit()
    {
        var job=ProjectMediaJob.MuxCopiedVideoAndAudio(1024);
        const string text="frame=25\ncodec failure";
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(text)))
            Assert.Equal(text,Encoding.UTF8.GetString(await job.ReadDiagnosticAsync(stream,32)));
        using(var oversized=new MemoryStream(new byte[33]))
            await Assert.ThrowsAsync<InvalidDataException>(()=>job.ReadDiagnosticAsync(oversized,32));
    }
    [Fact]
    public void ProgressUsesStderrAndKeepsAllMediaEncodingArguments()
    {
        var job=ProjectMediaJob.MuxCopiedVideoAndAudio(1024);var original=job.FileDescriptorArguments();
        var args=job.WithProgress(new Immediate(_=>{}),totalFrames:100).FileDescriptorArguments();
        Assert.Equal("pipe:2",args[Array.IndexOf(args,"-progress")+1]);
        Assert.Equal(original,args.Skip(5));
        Assert.Throws<ArgumentOutOfRangeException>(()=>job.WithProgress(new Immediate(_=>{})));
    }
    private sealed class Immediate(Action<double> action):IProgress<double>{public void Report(double value)=>action(value);}
    private sealed class SplitStream(string text):Stream
    {
        private readonly MemoryStream _data=new(Encoding.UTF8.GetBytes(text));
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {await Task.Yield();ct.ThrowIfCancellationRequested();return _data.Read(buffer.Span[..Math.Min(3,buffer.Length)]);}
        public override int Read(byte[] b,int offset,int count)=>throw new InvalidOperationException("Progress must use asynchronous pipe reads");
        public override void Flush()=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] b,int offset,int count)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){if(disposing)_data.Dispose();base.Dispose(disposing);}
    }
}
