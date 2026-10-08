// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using LibVLCSharp.Shared;

namespace ScreenRecorder.ProjectPreviewProbe;

// Isolated backend evaluation. Not a product player, and no application media paths are accepted.
internal static class VlcStreamProbe
{
    internal static async Task<int> RunAsync(string[] args)
    {
        string Argument(string key) => args.SkipWhile(a => a != key).Skip(1).FirstOrDefault()
            ?? throw new ArgumentException($"Required: {key}");
        var checks = new List<ProbeCheck>();
        var scratch = Directory.CreateTempSubdirectory("opencam-vlc-probe-").FullName;
        try
        {
            var ffmpeg = Argument("--ffmpeg");
            var profile = args.Contains("--profile") ? Argument("--profile") : "baseline";
            if (profile is not ("baseline" or "60fps" or "vfr" or "offset"))
                throw new ArgumentException("Unsupported probe profile.");
            var rate = profile is "60fps" or "vfr" ? 60 : 30;
            var extraFilter = profile switch { "vfr" => ",select=not(eq(mod(n\\,3)\\,1))",
                "offset" => ",setpts=PTS+2/TB", _ => "" };
            LibVLCSharp.Shared.Core.Initialize(Argument("--libvlc"));
            var fixture = Path.Combine(scratch, "frame-barcode.mkv");
            await BoundedProcess.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-n",
                "-f", "lavfi", "-i", $"nullsrc=size=320x180:rate={rate}:duration=4",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
                "-vf", "geq=lum='16+219*mod(floor(N/pow(2,floor(X/40))),2)':cb=128:cr=128" + extraFilter,
                "-af", profile == "offset" ? "volume=0.01,asetpts=PTS+2/TB" : "volume=0.01",
                "-fps_mode", "passthrough", "-c:v", "libx264", "-g", "120", "-bf", "0", "-c:a", "pcm_s16le", fixture], null, default);
            await using var input = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await SHA256.HashDataAsync(input);
            input.Position = 0;
            File.Move(fixture, fixture + ".bound");
            await File.WriteAllTextAsync(fixture, "Not the bound video");
            using var vlc = new LibVLC("--ignore-config", "--no-media-library", "--no-video-title-show",
                "--avcodec-hw=none", "--quiet");
            using var mediaInput = new StreamMediaInput(input);
            using var media = new Media(vlc, mediaInput);
            using var player = new MediaPlayer(vlc);
            using var output = new BarcodeVideoOutput(player);
            player.Volume = 1;
            try
            {
                if (!player.Play(media)) throw new InvalidDataException("Player rejected fixture.");
                await WaitUntil(() => output.DisplayCount >= 12, TimeSpan.FromSeconds(8));
                checks.Add(new("bound-stream-continuous-video", "PASS",
                    $"profile={profile}; {output.DisplayCount} actual decoded barcode frames, last ordinal {output.Ordinal}; original pathname replaced"));
                var audioStats = media.Statistics;
                checks.Add(new("native-audio-buffers", audioStats.PlayedAudioBuffers > 0 ? "PASS" : "FAIL",
                    $"decoded={audioStats.DecodedAudio}, played={audioStats.PlayedAudioBuffers}; native output statistics, not acoustic loopback"));
                player.SetPause(true);
                await Task.Delay(100);
                var beforeSeek = output.DisplayCount;
                // libvlc's timeline retains container timestamp origin; compare source PTS, not a guessed zero origin.
                var originMilliseconds = profile == "offset" ? 2000 : 0;
                player.Time = originMilliseconds + 1500;
                await WaitUntil(() => output.DisplayCount > beforeSeek, TimeSpan.FromSeconds(4));
                await Task.Delay(250);
                var ordinal = output.Ordinal;
                var expectedOrdinal = rate * 3 / 2;
                checks.Add(new("paused-precise-seek", ordinal == expectedOrdinal ? "PASS" : "FAIL",
                    $"Requested source {originMilliseconds + 1500}ms / frame {expectedOrdinal}, decoded barcode frame {ordinal}; settled 250ms, not inferred from player.Time"));
                beforeSeek = output.DisplayCount;
                for (var i = 0; i < 99; i++) player.Time = originMilliseconds + i % 3 * 1000;
                player.Time = originMilliseconds + 2000;
                await WaitUntil(() => output.DisplayCount > beforeSeek, TimeSpan.FromSeconds(4));
                await Task.Delay(250);
                checks.Add(new("rapid-seek-final-frame", output.Ordinal == rate * 2 ? "PASS" : "FAIL",
                    $"100 requests; final expected frame {rate * 2}, decoded {output.Ordinal}; does not yet prove stale UI publication suppression"));
                player.SetPause(false);
                var resumed = output.DisplayCount;
                await WaitUntil(() => output.DisplayCount >= resumed + 8, TimeSpan.FromSeconds(4));
                var watch = Stopwatch.StartNew();
                await Task.Run(player.Stop).WaitAsync(TimeSpan.FromSeconds(5));
                var stopped = output.DisplayCount;
                await Task.Delay(250);
                checks.Add(new("stop-joins-video-callbacks", output.DisplayCount == stopped ? "PASS" : "FAIL",
                    $"Stop took {watch.Elapsed.TotalMilliseconds - 250:F1}ms; no post-stop callbacks={output.DisplayCount == stopped}"));
                input.Position = 0;
                checks.Add(new("source-unchanged", hash.SequenceEqual(await SHA256.HashDataAsync(input)) ? "PASS" : "FAIL",
                    "SHA-256 checked through the original bound stream after playback"));
                if (output.Error is not null) throw new InvalidDataException(output.Error);
            }
            finally { await Task.Run(player.Stop); }
            checks.Add(new("audio-output/A-V/Windows", "NOT_TESTED",
                "Quiet real audio output requested; no acoustic or shared audio-clock measurement. Cross-clip, Windows, long-project and latest-seek stress gates remain."));
        }
        catch (Exception ex) { checks.Add(new("probe", "FAIL", ex.ToString())); }
        var failed = checks.Any(c => c.Status == "FAIL");
        Console.WriteLine(JsonSerializer.Serialize(new PreviewProbeResult("libvlc-stream", failed ? "FAIL" : "NOT_READY", checks),
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Evidence fixtures retained: {scratch}");
        return failed ? 1 : 2;
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan limit)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > limit) throw new TimeoutException("Expected decoded frame did not arrive.");
            await Task.Delay(10);
        }
    }

    private sealed class BarcodeVideoOutput : IDisposable
    {
        private readonly IntPtr _allocation = Marshal.AllocHGlobal(320 * 180 * 4 + 31);
        private readonly IntPtr _pixels;
        private readonly SemaphoreSlim _decode = new(1, 1);
        private readonly ConcurrentDictionary<IntPtr, int> _pictures = new();
        private int _serial, _displayCount, _ordinal;
        public string? Error;
        public int DisplayCount => Volatile.Read(ref _displayCount);
        public int Ordinal => Volatile.Read(ref _ordinal);
        public BarcodeVideoOutput(MediaPlayer player)
        {
            _pixels = new((_allocation.ToInt64() + 31) & ~31L);
            player.SetVideoFormat("RV32", 320, 180, 1280);
            player.SetVideoCallbacks(Lock, Unlock, Display);
        }
        private IntPtr Lock(IntPtr opaque, IntPtr planes)
        {
            _decode.Wait();
            Marshal.WriteIntPtr(planes, _pixels);
            return new(Interlocked.Increment(ref _serial));
        }
        private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes)
        {
            try
            {
                var ordinal = 0;
                for (var bit = 0; bit < 8; bit++)
                    if (Marshal.ReadByte(_pixels, 90 * 1280 + (bit * 40 + 20) * 4) > 128) ordinal |= 1 << bit;
                if (_pictures.Count >= 256) { Error = "Picture callback quota exceeded."; return; }
                _pictures[picture] = ordinal;
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { _decode.Release(); }
        }
        private void Display(IntPtr opaque, IntPtr picture)
        {
            if (_pictures.TryRemove(picture, out var ordinal))
            {
                Volatile.Write(ref _ordinal, ordinal);
                Interlocked.Increment(ref _displayCount);
            }
        }
        public void Dispose() { Marshal.FreeHGlobal(_allocation); _decode.Dispose(); }
    }
}
