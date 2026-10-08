// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScreenRecorder.ProjectPreviewProbe;

/// <summary>Bounded, decoded-media/device-clock diagnostic, not the product player.</summary>
internal static class IntegratedPlaybackProbe
{
    internal static async Task<int> RunAsync(string[] args)
    {
        string? Option(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        var ffmpeg = Option("--ffmpeg") ?? throw new ArgumentException("--ffmpeg is required");
        var helper = Option("--audio-helper") ?? throw new ArgumentException("--audio-helper is required");
        var scratch = Directory.CreateTempSubdirectory("opencam-integrated-clock-").FullName;
        var checks = new List<ProbeCheck>();
        try
        {
            var frames = new List<object>();
            using var pcm = new MemoryStream();
            var fingerprints = new List<(string Path, byte[] Hash)>();
            for (var clip = 0; clip < 2; clip++)
            {
                var origin = 2 + clip;
                var late = clip == 0 ? .2 : 0;
                var path = Path.Combine(scratch, $"clip-{clip}.mkv");
                await BoundedProcess.RunAsync(ffmpeg, ["-nostdin", "-hide_banner", "-loglevel", "error", "-n",
                    "-f", "lavfi", "-i", "testsrc2=size=64x36:rate=30:duration=0.8",
                    "-f", "lavfi", "-i", "aevalsrc=if(between(t\\,0.25\\,0.30)\\,0.01\\,0):s=48000:d=0.8",
                    "-filter_complex", FormattableString.Invariant(
                        $"[0:v]drawbox=c=white:t=fill:enable='between(t,{.25 + late},{.30 + late})',setpts=PTS+{origin}/TB[v];[1:a]asetpts=PTS+{origin + late}/TB[a]"),
                    "-map", "[v]", "-map", "[a]", "-c:v", "libx264", "-bf", "0", "-c:a", "pcm_f32le",
                    "-fps_mode", "passthrough", path], null, default);
                fingerprints.Add((path, SHA256.HashData(await File.ReadAllBytesAsync(path))));
                var raw = Path.Combine(scratch, $"clip-{clip}.rgba");
                var hashes = await BoundedProcess.RunAsync(ffmpeg, ["-nostdin", "-hide_banner", "-loglevel", "error",
                    "-copyts", "-i", path, "-map", "0:v:0", "-an", "-pix_fmt", "rgba", "-c:v", "rawvideo",
                    "-fps_mode", "passthrough", "-enc_time_base", "demux", "-f", "framehash", "-hash", "sha256", "pipe:1",
                    "-map", "0:v:0", "-an", "-pix_fmt", "rgba", "-c:v", "rawvideo", "-fps_mode", "passthrough",
                    "-f", "rawvideo", "-n", raw], null, default);
                var rows = hashes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var tb = rows.Single(l => l.StartsWith("#tb 0:")).Split(':')[1].Trim().Split('/');
                var scale = double.Parse(tb[0], CultureInfo.InvariantCulture) / double.Parse(tb[1], CultureInfo.InvariantCulture);
                var pixels = await File.ReadAllBytesAsync(raw);
                var index = 0;
                foreach (var row in rows.Where(l => !l.StartsWith('#')))
                {
                    var fields = row.Split(',', StringSplitOptions.TrimEntries);
                    var frame = pixels.AsSpan(index++ * 64 * 36 * 4, 64 * 36 * 4).ToArray();
                    if (!Convert.ToHexString(SHA256.HashData(frame)).Equals(fields[5], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Decoded frame payload/hash mismatch");
                    frames.Add(new { seconds = clip * .8 + long.Parse(fields[2]) * scale - origin,
                        rgba = Convert.ToBase64String(frame), clip });
                }
                if (index != 24 || pixels.Length != index * 64 * 36 * 4)
                    throw new InvalidDataException("Unexpected diagnostic frame count");
                var audio = Path.Combine(scratch, $"clip-{clip}.f32");
                await BoundedProcess.RunAsync(ffmpeg, ["-nostdin", "-hide_banner", "-loglevel", "error", "-copyts",
                    "-i", path, "-map", "0:a:0", "-af", FormattableString.Invariant(
                        $"atrim=start={origin}:end={origin + .8},asetpts=PTS-{origin}/TB,aresample=48000:first_pts=0,apad=whole_len=38400,atrim=end_sample=38400"),
                    "-ac", "1", "-ar", "48000", "-c:a", "pcm_f32le", "-f", "f32le", "-n", audio], null, default);
                var samples = await File.ReadAllBytesAsync(audio);
                if (samples.Length != 38400 * 4) throw new InvalidDataException("Unexpected PCM length");
                // The first audio marker must retain the .2s audio-stream offset.
                var first = Enumerable.Range(0, 38400).First(i => Math.Abs(BitConverter.ToSingle(samples, i * 4)) > .001);
                if (Math.Abs(first / 48000d - (.25 + late)) > .001)
                    throw new InvalidDataException("Late audio lost its source offset");
                pcm.Write(samples);
            }
            var manifest = Path.Combine(scratch, "playback.json");
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new {
                pcm = Convert.ToBase64String(pcm.ToArray()), frames, width = 64, height = 36 }));
            var result = await BoundedProcess.RunAsync(helper, ["--integrated", manifest], null, default);
            using var evidence = JsonDocument.Parse(result);
            var root = evidence.RootElement;
            checks.Add(new("device-clock/two-decoded-clips", root.GetProperty("status").GetString()!,
                result.Trim()));
            foreach (var file in fingerprints)
                if (!file.Hash.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(file.Path))))
                    throw new InvalidDataException("Source changed during playback");
            checks.Add(new("decoded-payloads/late-audio/source-hashes", "PASS",
                "48 actual RGBA frames match independent hashes; 1.6s decoded PCM; late audio retained; sources unchanged"));
            checks.Add(new("continuous-decoder/seek/GUI", "NOT_TESTED",
                "This bounded predecoded diagnostic is not streaming playback, seek/restart product integration, or visible GUI acceptance."));
        }
        catch (Exception ex) { checks.Add(new("integrated-clock", "FAIL", ex.Message)); }
        var failed = checks.Any(c => c.Status == "FAIL");
        Console.WriteLine(JsonSerializer.Serialize(new PreviewProbeResult("ffmpeg-integrated-macos",
            failed ? "FAIL" : "NOT_READY", checks), new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Evidence retained: {scratch}");
        return failed ? 1 : 2;
    }
}
