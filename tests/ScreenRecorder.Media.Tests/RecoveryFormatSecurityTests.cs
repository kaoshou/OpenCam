// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Remux;

namespace ScreenRecorder.Media.Tests;

public class RecoveryFormatSecurityTests
{
    [Fact]
    public async Task PlaylistRenamedToMkvCannotImportMediaOutsideSession()
    {
        var fixture = Directory.CreateTempSubdirectory("OpenCam-format-test-").FullName;
        try
        {
            var media = Path.Combine(fixture, "test-video.ts");
            var info = new ProcessStartInfo(FFmpegDiscovery.FindFFmpegExecutable()!)
            {
                UseShellExecute = false, RedirectStandardError = true
            };
            foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "color=size=64x64:duration=1:rate=25", "-c:v", "mpeg2video", "-f", "mpegts", media }) info.ArgumentList.Add(arg);
            using var generator = Process.Start(info)!;
            var errors = generator.StandardError.ReadToEndAsync();
            await generator.WaitForExitAsync();
            Assert.True(generator.ExitCode == 0, await errors);
            var session = Directory.CreateDirectory(Path.Combine(fixture, "session")).FullName;
            var disguised = Path.Combine(session, "recording.mkv");
            await File.WriteAllTextAsync(disguised, $"#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:1\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:1,\n{new Uri(media).AbsoluteUri}\n#EXT-X-ENDLIST\n");
            // Establish that the fixture contains valid media, not a broken encoder output.
            Assert.True((await new MediaFileProbe().ProbeAsync(media)).IsValid);
            var probe = await new MediaFileProbe().ProbeAsync(disguised);
            Assert.False(probe.IsValid);
            Assert.False(await new StreamCopyRemuxer().RemuxToMp4Async(disguised, Path.Combine(session, "out.mp4")));
        }
        finally { Directory.Delete(fixture, true); }
    }
}
