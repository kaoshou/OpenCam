// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace ScreenRecorder.Media.Projects;

public sealed partial class ProjectMediaJob
{
    private IProgress<double>? _progress;
    private long _progressFrames,_progressSamples;
    private double _lastReportedProgress;

    /// <summary>Observe completed frames or audio samples without mixing progress into media stdout.</summary>
    public ProjectMediaJob WithProgress(IProgress<double> progress,long totalFrames=0,long totalAudioSamples=0)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if(IsProbe||_exportArguments is null||totalFrames<0||totalAudioSamples<0||
            (totalFrames==0)==(totalAudioSamples==0))throw new ArgumentOutOfRangeException(nameof(totalFrames));
        _progress=progress;_progressFrames=totalFrames;_progressSamples=totalAudioSamples;_lastReportedProgress=0;
        return this;
    }

    internal async Task<byte[]> ReadDiagnosticAsync(Stream reader,int limit,CancellationToken ct=default)
    {
        using var diagnostic=new MemoryStream();
        using var line=new MemoryStream();
        var buffer=new byte[16384];
        void AddDiagnostic(ReadOnlySpan<byte> bytes)
        {
            if(diagnostic.Length+bytes.Length>limit)throw new InvalidDataException("Media diagnostic limit exceeded.");
            diagnostic.Write(bytes);
        }
        void CompleteLine(bool newline)
        {
            var bytes=line.GetBuffer().AsSpan(0,checked((int)line.Length));
            if(!ConsumeProgressLine(Encoding.UTF8.GetString(bytes).TrimEnd('\r')))
            {
                AddDiagnostic(bytes);
                if(newline)AddDiagnostic("\n"u8);
            }
            line.SetLength(0);
        }
        int count;
        while((count=await reader.ReadAsync(buffer,ct))!=0)
        {
            if(_progress is null){AddDiagnostic(buffer.AsSpan(0,count));continue;}
            for(var i=0;i<count;i++)
            {
                if(buffer[i]=='\n'){CompleteLine(true);continue;}
                if(line.Length>=limit)throw new InvalidDataException("Media diagnostic line limit exceeded.");
                line.WriteByte(buffer[i]);
            }
        }
        if(line.Length>0)CompleteLine(false);
        return diagnostic.ToArray();
    }

    private bool ConsumeProgressLine(string line)
    {
        var separator=line.IndexOf('=');
        if(separator<0)return false;
        var key=line[..separator];var value=line[(separator+1)..];
        var known=key is "frame" or "fps" or "bitrate" or "total_size" or "out_time_us" or "out_time_ms" or
            "out_time" or "dup_frames" or "drop_frames" or "speed" or "progress";
        if(!known&&key.StartsWith("stream_",StringComparison.Ordinal)&&key.EndsWith("_q",StringComparison.Ordinal))
        {
            var fields=key.Split('_');
            known=fields.Length==4&&int.TryParse(fields[1],out _)&&int.TryParse(fields[2],out _);
        }
        if(!known)return false;
        if(((key=="frame"&&_progressFrames>0)||(key=="out_time_us"&&_progressSamples>0))&&
            long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var completed)&&completed>=0)
        {
            var fraction=Math.Clamp(_progressFrames>0?(double)completed/_progressFrames:
                completed*48000d/(_progressSamples*1_000_000d),0,1);
            if(fraction>_lastReportedProgress){_lastReportedProgress=fraction;_progress!.Report(fraction);}
        }
        // Progress records are transient. Keeping them in the bounded error log
        // would incorrectly abort long exports once they accumulated 1 MB.
        return true;
    }
}
