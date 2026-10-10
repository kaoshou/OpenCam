using System.Text;
using System.Collections.Immutable;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public class ProjectExportStrategyTests
{
    internal static RecordingProject Project(int count = 1)
    {
        var id = Guid.NewGuid();
        return new() { ProjectId = Guid.NewGuid(), Name = "recording", Sessions = ["s"],
            Canvas = new(64, 36, new(30, 1)), Sources = [new() { Id = id, SessionId = "s",
                RelativePath = "sources/a.mkv", Sha256 = new('a', 64), FileSize = 1000,
                Width = 64, Height = 36, VideoCodec = "h264", AudioCodec = "aac",
                Timing = new(new(1, 1000), 0, 1000) }],
            Clips = Enumerable.Range(0, count).Select(i => new ProjectClip { Id = Guid.NewGuid(),
                SourceId = id, Name = "part", InPts = 0, OutPts = 1000 }).ToImmutableArray() };
    }

    internal static ProjectSourceEvidence Evidence(string? audio = "aac") => new(
        new("h264", "SHA256:abc", 64, 36, "yuv420p", new(1, 1000), "30/1", 0, "",
            30, 0, 1000, 33, true, true),
        audio is null ? null : new(audio, "SHA256:def", 0, 0, "", new(1, 1000), "0/0", 48000, "stereo",
            47, 0, 1000, 22, true, true));

    [Theory]
    [InlineData("aac", ProjectExportStrategy.CopySingle)]
    [InlineData(null, ProjectExportStrategy.CopySingle)]
    [InlineData("pcm_f32le", ProjectExportStrategy.ConvertAudioOnly)]
    public void WholeVideoDoesNotRequireEncoding(string? audio, ProjectExportStrategy want)
        => Assert.Equal(want, ProjectExportPlanner.Select(ProjectRenderPlan.Create(Project()), [Evidence(audio)]).Strategy);

    [Fact]
    public void CompatibleWholeSegmentsCanBeReorderedWithoutEncoding()
        => Assert.Equal(ProjectExportStrategy.ConcatCopy,
            ProjectExportPlanner.Select(ProjectRenderPlan.Create(Project(2)), [Evidence(null), Evidence(null)]).Strategy);

    [Fact]
    public void AacPaddingRequiresOnlyAudioNormalization()
        => Assert.Equal(ProjectExportStrategy.ConcatConvertAudio,
            ProjectExportPlanner.Select(ProjectRenderPlan.Create(Project(2)), [Evidence(), Evidence()]).Strategy);

    [Theory]
    [InlineData("extra")][InlineData("fps")][InlineData("audio")][InlineData("key")]
    public void IncompatibleActualStreamsRender(string change)
    {
        var second = Evidence();
        second = change switch {
            "extra" => second with { Video = second.Video with { InitializationHash = "different" } },
            "fps" => second with { Video = second.Video with { FrameRate = "60/1" } },
            "audio" => second with { Audio = null },
            _ => second with { Video = second.Video with { StartsWithKeyframe = false } }
        };
        Assert.Equal(ProjectExportStrategy.Render, ProjectExportPlanner.Select(
            ProjectRenderPlan.Create(Project(2)), [Evidence(), second]).Strategy);
    }

    [Fact]
    public void TrimAndEffectsRenderButNameDoesNotMatter()
    {
        var project = Project();
        foreach (var clip in new[] { project.Clips[0] with { InPts = 1 }, project.Clips[0] with { Muted = true },
            project.Clips[0] with { Scale = 2 }, project.Clips[0] with { Volume = .5 } })
            Assert.Equal(ProjectExportStrategy.Render, ProjectExportPlanner.Select(
                ProjectRenderPlan.Create(project with { Clips = [clip] }), [Evidence()]).Strategy);
        Assert.Equal(ProjectExportStrategy.CopySingle, ProjectExportPlanner.Select(
            ProjectRenderPlan.Create(project with { Name = "renamed" }), [Evidence()]).Strategy);
    }

    [Fact]
    public async Task PacketParserAcceptsReorderedPtsButRejectsDecreasingDts()
    {
        const string stream = "stream|index=0|codec_name=h264|codec_type=video|width=64|height=36|pix_fmt=yuv420p|time_base=1/1000|r_frame_rate=30/1|extradata_hash=SHA256:abc\n";
        const string packets = "packet|stream_index=0|pts=0|dts=-66|duration=33|flags=K_\npacket|stream_index=0|pts=66|dts=-33|duration=33|flags=__\npacket|stream_index=0|pts=33|dts=0|duration=33|flags=__\n";
        var good = await Parse(packets + stream);
        Assert.Equal(3, good.Video.PacketCount);
        Assert.Equal(99, good.Video.EndPts);
        Assert.True(good.Video.MonotonicDts);
        var bad = await Parse(packets.Replace("dts=0", "dts=-99") + stream);
        Assert.False(bad.Video.MonotonicDts);
        await Assert.ThrowsAsync<InvalidDataException>(() => Parse(packets.Replace("duration=33", "duration=9223372036854775807") + stream));
        await Assert.ThrowsAsync<InvalidDataException>(() => Parse(new string('x', 65537)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("|duration=N/A")]
    [InlineData("|duration=0")]
    [InlineData("|duration=-1")]
    public async Task UnavailablePacketDurationRequiresRendering(string duration)
    {
        await Assert.ThrowsAsync<ProjectRemuxIncompatibleException>(() =>
            Parse($"packet|stream_index=0|pts=0|dts=0{duration}|flags=K_\n"));
    }

    [Fact]
    public async Task InteriorTimingChangeCannotHideBehindMatchingPacketPayloadsAndEndpoints()
    {
        var hash = "SHA256:" + new string('a', 64);
        var packets = string.Concat(new[] { 0, 33, 66 }.Select(t =>
            $"packet|stream_index=0|pts={t}|dts={t}|duration=33|flags=K_|data_hash={hash}\n"));
        const string stream = "stream|index=0|codec_name=h264|codec_type=video|width=64|height=36|pix_fmt=yuv420p|time_base=1/1000|r_frame_rate=30/1\n";
        var original = (await Parse(packets + stream)).Video;
        var moved = (await Parse(packets.Replace("pts=33", "pts=40") + stream)).Video;
        Assert.Equal(original.PacketHash, moved.PacketHash);
        Assert.Equal(original.FirstPts, moved.FirstPts);
        Assert.Equal(original.EndPts, moved.EndPts);
        Assert.NotEqual(original.PresentationTimingHash, moved.PresentationTimingHash);
    }

    internal static Task<ProjectSourceEvidence> Parse(string value)
        => ProjectPacketInspector.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(value)), default);

    [Fact]
    public async Task LongPacketStreamRetainsSummaryAndCancellationWorks()
    {
        var text = new StringBuilder();
        for (var i = 0; i < 100000; i++) text.Append($"packet|stream_index=0|pts={i*33}|dts={i*33}|duration=33|flags=K_\n");
        text.Append("stream|index=0|codec_name=h264|codec_type=video|width=64|height=36|pix_fmt=yuv420p|time_base=1/1000|r_frame_rate=30/1|extradata_hash=SHA256:abc\n");
        var result = await Parse(text.ToString());
        Assert.Equal(100000, result.Video.PacketCount);
        Assert.Equal(3300000, result.Video.EndPts);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectPacketInspector.ParseAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(text.ToString())), cancel.Token));
    }

    [Fact]
    public void ExcessiveDescriptorsOrChangedRangeCannotTakeFastPath()
    {
        Assert.Equal(ProjectExportStrategy.Render, ProjectExportPlanner.Select(ProjectRenderPlan.Create(Project(129)),
            Enumerable.Repeat(Evidence(), 129).ToArray()).Strategy);
        Assert.Equal(ProjectExportStrategy.Render, ProjectExportPlanner.Select(ProjectRenderPlan.Create(Project()),
            [Evidence() with { Video = Evidence().Video with { EndPts = 2000 } }]).Strategy);
    }
}
