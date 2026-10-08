// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.ProjectPreviewProbe;

var candidate = args.SkipWhile(a => a != "--candidate").Skip(1).FirstOrDefault() ?? "none";
if (candidate is not ("ffmpeg-pipe" or "ffmpeg-fd-macos"))
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
    var originalHash = await SHA256.HashDataAsync(source);
    source.Position = 0;
    // A decoder that reopens the old name must fail this test, rather than reading a replacement.
    var movedFixture = fixture + ".bound";
    File.Move(fixture, movedFixture);
    await File.WriteAllTextAsync(fixture, "Replacement path is not the bound video.");
    await using var decoder = new FfmpegPipeCandidate(ffmpeg, candidate == "ffmpeg-fd-macos");
    await decoder.OpenAsync(source, default);
    var frame = await decoder.SeekAsync(2200, new(1, 1000), default);
    var reference = await BoundedProcess.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-copyts",
        "-i", movedFixture, "-map", "0:v:0", "-vf", "select=eq(n\\,6)", "-frames:v", "1",
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
    source.Position = 0;
    Check("source-retained", source.CanRead && originalHash.SequenceEqual(await SHA256.HashDataAsync(source)),
        "Original file SHA-256 unchanged; source remains open after rename and Stop");
    if (args.Contains("--extended"))
    {
        if (candidate == "ffmpeg-fd-macos")
        {
            await ExpectRejected<InvalidDataException>("child-failure", () =>
                MacFileDescriptorProcess.RunAsync("/usr/bin/false", [], source, default));
            await ExpectRejected<InvalidDataException>("child-output-quota", () =>
                MacFileDescriptorProcess.RunAsync("/usr/bin/yes", [], source, default));
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await ExpectRejected<OperationCanceledException>("running-child-cancellation", () =>
                MacFileDescriptorProcess.RunAsync("/bin/sleep", ["10"], source, cancel.Token));
        }
        await using var corruptInput = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var corruptReader = new FfmpegPipeCandidate(ffmpeg, candidate == "ffmpeg-fd-macos");
        await corruptReader.OpenAsync(corruptInput, default);
        await ExpectRejected<InvalidDataException>("corrupt-media-rejected", () => corruptReader.SeekAsync(0, new(1,1000), default));
        await ExtendedMatrix();
    }
    var audioHelper = args.SkipWhile(a => a != "--audio-helper").Skip(1).FirstOrDefault();
    if (audioHelper is not null)
    {
        var result = await BoundedProcess.RunAsync(audioHelper, ["--self-test"], null, default);
        using var audio = JsonDocument.Parse(result);
        Check("native-audio-device-stop", audio.RootElement.GetProperty("playedFrames").GetInt64() > 0 &&
            audio.RootElement.GetProperty("stopped").GetBoolean() &&
            audio.RootElement.GetProperty("restartPassed").GetBoolean(), result.Trim());
    }
    checks.Add(new("integrated-A/V-playback", "NOT_IMPLEMENTED", "Standalone device test is not a shared video/audio clock or stop-before-record integration."));
    checks.Add(new("Windows/native-assets", "BLOCKED", "Windows runtime and distributable FFmpeg version not tested; macOS executable is the explicit --ffmpeg argument."));
    checks.Add(new("1080p/200clips/integrated-editor", "NOT_TESTED", "Low-resolution decoder fixtures cannot establish complete editor performance or cross-clip synchronization."));
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

async Task ExpectRejected<TException>(string name, Func<Task> action) where TException : Exception
{
    try { await action(); Check(name, false, "Invalid operation unexpectedly succeeded"); }
    catch (TException) { Check(name, true, typeof(TException).Name + "; child lifecycle returned without success"); }
}

async Task ExtendedMatrix()
{
    foreach (var item in new[] {
        (Name: "60FPS", Rate: "60", Filter: "null", Pts: 217L, Ordinal: 13, Denominator: 1000, Extension: ".mkv"),
        (Name: "VFR", Rate: "60", Filter: "select=not(eq(mod(n\\,3)\\,1))", Pts: 250L, Ordinal: 10, Denominator: 1000, Extension: ".mkv"),
        (Name: "negative-PTS", Rate: "30", Filter: "setpts=PTS-1/TB", Pts: -72000L, Ordinal: 6, Denominator: 90000, Extension: ".ts") })
    {
        var path = Path.Combine(scratch, item.Name + item.Extension);
        await BoundedProcess.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-n",
            "-f", "lavfi", "-i", $"testsrc2=size=320x180:rate={item.Rate}:duration=2",
            "-vf", item.Filter, "-fps_mode", "passthrough", "-avoid_negative_ts", "disabled",
            "-c:v", "libx264", "-g", "60", "-bf", "0", "-an",
            ..(item.Extension == ".ts" ? new[] { "-muxdelay", "0", "-muxpreload", "0" } : Array.Empty<string>()), path], null, default);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var reader = new FfmpegPipeCandidate(ffmpeg, candidate == "ffmpeg-fd-macos");
        await reader.OpenAsync(input, default);
        var actual = await reader.SeekAsync(item.Pts, new(1, item.Denominator), default);
        var reference = await BoundedProcess.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-copyts",
            "-i", path, "-map", "0:v:0", "-vf", $"select=eq(n\\,{item.Ordinal})", "-frames:v", "1",
            "-fps_mode", "passthrough", "-enc_time_base", "demux", "-c:v", "rawvideo", "-pix_fmt", "yuv420p",
            "-f", "framehash", "-hash", "sha256", "pipe:1"], null, default);
        var expected = FfmpegPipeCandidate.Parse(reference, item.Pts, new(1, item.Denominator));
        Check(item.Name, actual.ActualPts == item.Pts && expected.ActualPts == item.Pts && actual.FrameHash == expected.FrameHash,
            $"requested={item.Pts}, actual={actual.ActualPts}; ordinal hash matches={actual.FrameHash == expected.FrameHash}");
    }
    var longPath = Path.Combine(scratch, "two-hour-low-resolution.mkv");
    await BoundedProcess.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-n",
        "-f", "lavfi", "-i", "testsrc2=size=64x36:rate=30:duration=7200", "-c:v", "libx264",
        "-preset", "ultrafast", "-g", "300", "-bf", "0", "-an", longPath], null, default);
    await using var longInput = new FileStream(longPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    await using var longReader = new FfmpegPipeCandidate(ffmpeg, candidate == "ffmpeg-fd-macos");
    await longReader.OpenAsync(longInput, default);
    var times = new List<double>();
    for (var index = 0; index < 20; index++)
    {
        var watch = Stopwatch.StartNew();
        var frame = await longReader.SeekAsync(7199200, new(1, 1000), default);
        times.Add(watch.Elapsed.TotalMilliseconds);
        if (frame.ActualPts != 7199200) throw new InvalidDataException("Long seek selected wrong PTS.");
    }
    checks.Add(new("two-hour/long-GOP-seek", "MEASURED",
        $"64x36 diagnostic, 30FPS, GOP300, target7199.2s, 20 seeks p95={times.Order().ElementAt(18):F1}ms; NOT 1080p/200-clip UI acceptance"));
}
