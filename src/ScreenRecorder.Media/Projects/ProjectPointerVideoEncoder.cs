// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.FFmpeg;

namespace ScreenRecorder.Media.Projects;

/// <summary>Encodes compositor frames through bounded OS pipes, without a raw-video disk spool.
/// The caller owns output and the producer receives only an exact-length raw RGBA sink.</summary>
public sealed class ProjectPointerVideoEncoder
{
    public async Task EncodeAsync(ProjectCanvas canvas,int crf,long frameCount,Stream output,
        Func<Stream,CancellationToken,Task> produce,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        if(canvas.Width is <2 or >4096 || canvas.Height is <2 or >4096 || canvas.Width%2!=0 || canvas.Height%2!=0 ||
            canvas.Fps.Numerator<=0 || canvas.Fps.Denominator<=0 || canvas.Fps.Numerator>240L*canvas.Fps.Denominator ||
            crf is <0 or >51 || frameCount<=0 || !output.CanWrite)
            throw new InvalidDataException("Invalid pointer video encoder parameters.");
        var bytes=checked(frameCount*canvas.Width*canvas.Height*4);
        var limit=checked(bytes+frameCount*65536);
        var path=FFmpegDiscovery.FindFFmpegExecutable() ?? throw new FileNotFoundException("FFmpeg is unavailable.");
        var start=new ProcessStartInfo(path) { UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var arg in new[] {"-nostdin","-v","error","-f","rawvideo","-pix_fmt","rgba","-video_size",
            $"{canvas.Width}x{canvas.Height}","-framerate",$"{canvas.Fps.Numerator}/{canvas.Fps.Denominator}","-i","pipe:0",
            "-an","-sn","-dn","-frames:v",frameCount.ToString(CultureInfo.InvariantCulture),"-c:v","libx264",
            "-preset","veryfast","-crf",crf.ToString(CultureInfo.InvariantCulture),"-bf","0","-pix_fmt","yuv420p",
            "-threads","2","-f","h264","pipe:1"}) start.ArgumentList.Add(arg);
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token=lifetime.Token;
        using var process=Process.Start(start) ?? throw new IOException("Cannot start pointer video encoder.");
        var failed=new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work=Task.WhenAll(Guard(Feed),Guard(Copy),Guard(Drain),Guard(()=>process.WaitForExitAsync(token)));
        try
        {
            await Task.WhenAny(work,failed.Task);
            // A failed reader/writer must not wait for its siblings blocked on full pipes.
            if(failed.Task.IsCompleted) ExceptionDispatchInfo.Capture(await failed.Task).Throw();
            await work;
            if(process.ExitCode!=0) throw new InvalidDataException("Pointer video encode failed.");
        }
        finally
        {
            if(!process.HasExited) process.Kill(true);
            lifetime.Cancel();
            try { await work.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch(Exception) when(work.IsCompleted) { _=work.Exception; }
        }
        async Task Guard(Func<Task> action)
        {
            try { await action(); }
            catch(Exception error) { failed.TrySetResult(error); throw; }
        }
        async Task Feed()
        {
            try
            {
                var sink=new ExactSink(process.StandardInput.BaseStream,bytes);
                await produce(sink,token);
                if(sink.Count!=bytes) throw new InvalidDataException("Incomplete compositor video frames.");
                await sink.FlushAsync(token);
            }
            finally { process.StandardInput.Close(); }
        }
        async Task Copy()
        {
            var buffer=new byte[65536]; long count=0; int read;
            while((read=await process.StandardOutput.BaseStream.ReadAsync(buffer,token))!=0)
            {
                count=checked(count+read);
                if(count>limit) throw new InvalidDataException("Pointer encoder exceeded output limit.");
                await output.WriteAsync(buffer.AsMemory(0,read),token);
            }
            if(count==0) throw new InvalidDataException("Pointer encoder produced no video.");
        }
        async Task Drain()
        {
            var buffer=new char[2048];
            while(await process.StandardError.ReadAsync(buffer.AsMemory(),token)>0) { }
        }
    }

    private sealed class ExactSink(Stream inner,long limit):Stream
    {
        public long Count {get;private set;}
        private void Reserve(int count)
        { if(count<0 || Count>limit-count) throw new InvalidDataException("Too many compositor video bytes."); Count+=count; }
        public override void Write(byte[] buffer,int offset,int count) { Reserve(count); inner.Write(buffer,offset,count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Reserve(buffer.Length); inner.Write(buffer); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default)
        { Reserve(buffer.Length); await inner.WriteAsync(buffer,ct); }
        public override Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken ct)=>WriteAsync(buffer.AsMemory(offset,count),ct).AsTask();
        public override void Flush()=>inner.Flush();
        public override Task FlushAsync(CancellationToken ct)=>inner.FlushAsync(ct);
        public override bool CanWrite=>true;
        public override bool CanRead=>false;
        public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position {get=>Count;set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();
        public override void SetLength(long v)=>throw new NotSupportedException();
    }
}
