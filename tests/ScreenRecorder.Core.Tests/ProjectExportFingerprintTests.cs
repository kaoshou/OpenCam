using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectExportFingerprintTests
{
    [Fact]
    public void OnlyContentChangesInvalidateFingerprint()
    {
        var source = new ProjectSource { Id = Guid.NewGuid(), SessionId = "session", RelativePath = "sources/a.mkv",
            FileSize = 5, Sha256 = new string('a',64), Width = 100, Height = 100, VideoCodec = "h264", Timing = new(new(1,1000),0,1000) };
        var p = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "test", Sessions = ["session"], Sources = [source],
            Clips = [new() { Id = Guid.NewGuid(), SourceId = source.Id, Name = "clip", OutPts = 1000 }] };
        var fingerprint = ProjectExportFingerprint.Compute(p, "h264-aac-v1");
        Assert.Equal(fingerprint, ProjectExportFingerprint.Compute(p with { Name = "rename", Revision = 20,
            AutoExportOnStop = false, ViewState = new(100), Clips = [p.Clips[0] with { Name = "renamed clip" }] }, "h264-aac-v1"));
        Assert.NotEqual(fingerprint, ProjectExportFingerprint.Compute(p with { Clips = [p.Clips[0] with { OutPts = 500 }] }, "h264-aac-v1"));
        Assert.NotEqual(fingerprint, ProjectExportFingerprint.Compute(p with { Clips = [p.Clips[0] with { Muted = true }] }, "h264-aac-v1"));
    }

    [Fact]
    public async Task SameSizeReplacementInvalidatesReceipt()
    {
        var root = Path.Combine(Path.GetTempPath(), "OpenCamReceipt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var h = await new JsonProjectStore().CreateAsync(root, "receipt");
            var output = Path.Combine(root, "test.mp4");
            await File.WriteAllBytesAsync(output, [1,2,3,4]);
            await ProjectExportReceiptStore.WriteAsync(h, h.Current, output);
            Assert.NotNull(await ProjectExportReceiptStore.FindVerifiedAsync(h, h.Current, root));
            await File.WriteAllBytesAsync(output, [4,3,2,1]);
            Assert.Null(await ProjectExportReceiptStore.FindVerifiedAsync(h, h.Current, root));
        }
        finally { Directory.Delete(root, true); }
    }
}
