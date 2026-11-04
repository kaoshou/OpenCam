// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
namespace ScreenRecorder.Media.Projects;
public sealed class PreviewAudioMeter
{
    private readonly object _sync=new();
    private double _peak,_squares;
    private long _count;
    public void Observe(ReadOnlySpan<byte> pcm)
    {
        if(pcm.Length%8!=0) throw new ArgumentException("Expected complete float32 stereo PCM.",nameof(pcm));
        double peak=0,squares=0; long count=0;
        foreach(var sample in MemoryMarshal.Cast<byte,float>(pcm))
        {
            if(!float.IsFinite(sample)) continue;
            peak=Math.Max(peak,Math.Abs(sample)); squares+=(double)sample*sample; count++;
        }
        lock(_sync){_peak=Math.Max(_peak,peak);_squares+=squares;_count+=count;}
    }
    public (double Peak,double Rms,bool Overload)? SnapshotAndReset()
    {
        lock(_sync)
        {
            if(_count==0)return null;
            var level=(Math.Clamp(_peak,0,1),Math.Clamp(Math.Sqrt(_squares/_count),0,1),_peak>=1);
            _peak=0;_squares=0;_count=0;return level;
        }
    }
}
