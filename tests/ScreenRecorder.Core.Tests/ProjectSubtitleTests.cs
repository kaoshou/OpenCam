// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Core.Tests;

public class ProjectSubtitleTests
{
    [Theory]
    [InlineData(".srt", "1\n00:00:01,000 --> 00:00:02,000\n第一行\n第二行\n")]
    [InlineData(".vtt", "WEBVTT\n\nintro\n00:01.000 --> 00:02.000 align:start\n第一行\n第二行\n")]
    public void ParsesPreciseCueTimesAndMultilineText(string extension, string text)
    {
        var cue = Assert.Single(ProjectSubtitleParser.Parse(Encoding.UTF8.GetBytes(text), extension));
        Assert.Equal(10000000, cue.StartTicks);
        Assert.Equal(20000000, cue.EndTicks);
        Assert.Equal("第一行\n第二行", cue.Text);
    }

    [Fact]
    public void OverlapSeekAndEndFenceDoNotLeaveStaleText()
    {
        ProjectSubtitleCue[] cues = [new(10000000, 30000000, "A"), new(20000000, 40000000, "B")];
        Assert.Equal("A\nB", ProjectSubtitleTimeline.ActiveText(cues, 25000000));
        Assert.Equal("B", ProjectSubtitleTimeline.ActiveText(cues, 30000000));
        Assert.Equal("", ProjectSubtitleTimeline.ActiveText(cues, 0));
        Assert.Equal("", ProjectSubtitleTimeline.ActiveText(cues, 40000000));
    }

    [Fact]
    public void Utf16BomIsAcceptedWithoutGuessingLegacyEncoding()
    {
        var text = "1\n00:00:01,000 --> 00:00:02,000\n教材";
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();
        Assert.Equal("教材", Assert.Single(ProjectSubtitleParser.Parse(bytes, ".srt")).Text);
        Assert.Throws<InvalidDataException>(() => ProjectSubtitleParser.Parse(new byte[] { 0xff, 0xff, 0xff }, ".srt"));
    }

    [Theory]
    [InlineData("lesson.zh-tw.srt", "zh-TW")]
    [InlineData("lesson.zh_tw.vtt", "zh-TW")]
    [InlineData("en.srt", "en")]
    [InlineData("lesson.en-US.vtt", "en-US")]
    [InlineData("lesson.ko.srt", "ko")]
    [InlineData("engineering.srt", null)]
    [InlineData("unknown.srt", null)]
    public void LanguageDetectionUsesTokensNotSubstrings(string file, string? expected) =>
        Assert.Equal(expected, ProjectSubtitleLanguage.Infer(file));

    [Theory]
    [InlineData("1\n00:00:02,000 --> 00:00:01,000\nbad")]
    [InlineData("1\n00:60:01,000 --> 00:60:02,000\nbad")]
    [InlineData("1\ninvalid --> 00:00:02,000\nbad")]
    [InlineData("WEBVTT\n\nNOTE empty")]
    public void MalformedOrEmptySubtitlesAreRejected(string text) =>
        Assert.Throws<InvalidDataException>(() => ProjectSubtitleParser.Parse(Encoding.UTF8.GetBytes(text), ".vtt"));

    [Fact]
    public void FileAndCueLimitsRejectRatherThanTruncate()
    {
        Assert.Throws<InvalidDataException>(() => ProjectSubtitleParser.Parse(new byte[10 * 1024 * 1024 + 1], ".srt"));
        var text = "1\n00:00:01,000 --> 00:00:02,000\n" + new string('x', 10001);
        Assert.Throws<InvalidDataException>(() => ProjectSubtitleParser.Parse(Encoding.UTF8.GetBytes(text), ".srt"));
    }

    [Fact]
    public void TrackCueCountIsBounded()
    {
        var text = string.Concat(Enumerable.Repeat("00:01.000 --> 00:02.000\nx\n\n", 100001));
        Assert.Throws<InvalidDataException>(() => ProjectSubtitleParser.Parse(Encoding.UTF8.GetBytes(text), ".vtt"));
    }

    [Fact]
    public void CustomLanguageRequiresAlphabeticPrimaryCode() =>
        Assert.Throws<InvalidDataException>(() => ProjectSubtitleLanguage.Normalize("12-TW"));
}
