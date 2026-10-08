// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Diagnostics;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.ProjectPreviewProbe;

var candidate = args.SkipWhile(a => a != "--candidate").Skip(1).FirstOrDefault() ?? "none";
if (candidate != "ffmpeg-pipe")
{
    Console.WriteLine(JsonSerializer.Serialize(new PreviewProbeResult(candidate, "NOT_IMPLEMENTED",
        [new("backend", "NOT_IMPLEMENTED", "No measured preview backend; cannot claim playback or audio stop.")])));
    return 2;
}
var ffmpeg = args.SkipWhile(a => a != "--ffmpeg").Skip(1).FirstOrDefault() ?? "ffmpeg";
var scratch = Directory.CreateTempSubdirectory("opencam-preview-probe-").FullName;
var checks = new List<ProbeCheck>();
try
{
    var fixture = Path.Combine(scratch, "nonzero-pts.mkv");
    await BoundedProcess.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-n",
        "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30:duration=1",
        "-vf", "setpts=PTS+2/TB", "-fps_mode", "passthrough",
        "-c:v", "libx264", "-g", "30", "-bf", "0", "-an", fixture], null, default);
    await using var source = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.Read);
    await using var decoder = new FfmpegPipeCandidate(ffmpeg);
    await decoder.OpenAsync(source, default);
    var frame = await decoder.SeekAsync(2200, new(1, 1000), default);
    var reference = await BoundedProcess.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-copyts",
        "-i", fixture, "-map", "0:v:0", "-vf", "select=eq(n\\,6)", "-frames:v", "1",
        "-fps_mode", "passthrough", "-enc_time_base", "demux", "-c:v", "rawvideo", "-pix_fmt", "yuv420p",
        "-f", "framehash", "-hash", "sha256", "pipe:1"], null, default);
    var expected = FfmpegPipeCandidate.Parse(reference, 2200, new(1,1000));
    Check("nonzero-PTS/frame-content", frame.ActualPts == 2200 && frame.FrameHash == expected.FrameHash,
        $"actualPts={frame.ActualPts}; independent ordinal-frame hash matches={frame.FrameHash == expected.FrameHash}");
    var samples = new List<double>();
    for (var i = 0; i < 10; i++)
    {
        var watch = Stopwatch.StartNew();
        await decoder.SeekAsync(2400, new(1,1000), default);
        samples.Add(watch.Elapsed.TotalMilliseconds);
    }
    checks.Add(new("short-fixture-seek", "MEASURED", $"10 seeks; max={samples.Max():F1}ms; NOT a long-project benchmark"));
    var tasks = Enumerable.Range(0, 100).Select(i => SeekOnce(2000 + i % 20 * 33)).ToArray();
    await Task.WhenAll(tasks);
    Check("latest-seek-wins", tasks[^1].Result && tasks.Take(99).All(t => !t.Result), "100 consecutive requests; superseded seeks canceled");
    var pending = SeekOnce(2600);
    await decoder.StopAsync(default);
    Check("stop-joins-decoder", !await pending, "in-flight seek canceled and subprocess joined");
    Check("source-retained", source.CanRead && source.Length > 0, "Stop leaves original source open and unchanged");
    checks.Add(new("audio-playback/stop", "NOT_IMPLEMENTED", "No PCM sink or A/V clock; no claim that preview audio is safe for recording."));
    checks.Add(new("Windows/native-assets", "BLOCKED", "Mac-only probe; Windows runtime and distributable FFmpeg version not tested."));
    checks.Add(new("2h/200clips/VFR/60FPS", "NOT_TESTED", "Short CFR fixture cannot establish production performance or variable-frame correctness."));
    async Task<bool> SeekOnce(long pts)
    {
        try { await decoder.SeekAsync(pts, new(1,1000), default); return true; }
        catch (OperationCanceledException) { return false; }
    }
}
catch (Exception ex) { checks.Add(new("probe", "FAIL", ex.Message)); }
Console.WriteLine(JsonSerializer.Serialize(new PreviewProbeResult(candidate,
    checks.Any(c => c.Status == "FAIL") ? "FAIL" : "NOT_READY", checks), new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Evidence fixtures retained: {scratch}");
return checks.Any(c => c.Status == "FAIL") ? 1 : 2;
void Check(string name, bool passed, string detail) => checks.Add(new(name, passed ? "PASS" : "FAIL", detail));
