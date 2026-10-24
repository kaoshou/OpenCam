using System.Text.Json;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Tests;

public class TeachingPointerOptionsTests
{
    [Theory]
    [InlineData("{}", 0, 1)]
    [InlineData("{\"PresentationPointer\":{\"Style\":3,\"Size\":2}}", 3, 2)]
    [InlineData("{\"PresentationPointer\":{\"Style\":999,\"Size\":-1}}", 0, 1)]
    [InlineData("{\"PresentationPointer\":null}", 0, 1)]
    public void UserSettingsRoundTripPreservesSafeStyleAndSize(string json, int style, int size)
    {
        var settings = JsonSerializer.Deserialize<UserSettings>(json)!;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(settings));
        Assert.True(document.RootElement.TryGetProperty("PresentationPointer", out var pointer));
        Assert.Equal(style, pointer.GetProperty("Style").GetInt32());
        Assert.Equal(size, pointer.GetProperty("Size").GetInt32());
    }

    [Fact]
    public void SharedArtworkContractIsAvailableToBothPlatforms()
    {
        Assert.NotNull(typeof(UserSettings).Assembly.GetType("ScreenRecorder.Core.Models.TeachingPointerArtwork"));
    }

    [Fact]
    public void PausedOptionsApplyOnlyToNextSegmentWithoutChangingVideoOrAudioCapture()
    {
        var config = new RecordingConfiguration { NativeCaptureEnabled = true, Fps = 30,
            CursorEffect = ScreenRecorder.Core.Enums.CursorEffectMode.PresentationStick };
        var options = new TeachingPointerOptions(TeachingPointerStyle.HighContrast, TeachingPointerSize.Large);
        config.ApplyPausedSettings(new RecordingConfiguration { PresentationPointer = options,
            AudioSource = ScreenRecorder.Core.Enums.AudioSourceType.MicrophoneOnly,
            CursorEffect = ScreenRecorder.Core.Enums.CursorEffectMode.PresentationStick });
        Assert.Equal(options, config.PresentationPointer);
        Assert.True(config.NativeCaptureEnabled);
        Assert.Equal(30, config.Fps);
        Assert.Equal(ScreenRecorder.Core.Enums.AudioSourceType.MicrophoneOnly, config.AudioSource);
        var copy = JsonSerializer.Deserialize<RecordingConfiguration>(JsonSerializer.Serialize(config))!;
        Assert.Equal(options, copy.PresentationPointer);
    }

    [Theory]
    [InlineData(TeachingPointerSize.Small, .82)]
    [InlineData(TeachingPointerSize.Standard, 1)]
    [InlineData(TeachingPointerSize.Large, 1.23)]
    public void SizesUseTheApprovedRelativeScale(TeachingPointerSize size, double scale) =>
        Assert.Equal(scale, new TeachingPointerOptions(Size: size).Scale);

    [Fact]
    public void EveryStyleAndSizeHasCachedTransparentArtworkWithPaddedTipGeometry()
    {
        var fingerprints = new HashSet<string>();
        foreach (var style in Enum.GetValues<TeachingPointerStyle>())
        foreach (var size in Enum.GetValues<TeachingPointerSize>())
        {
            var options = new TeachingPointerOptions(style,size);
            var art = TeachingPointerArtwork.Get(options);
            Assert.Same(art, TeachingPointerArtwork.Get(options));
            Assert.Equal((int)Math.Ceiling(140 * options.Scale),art.Width);
            Assert.Equal((int)Math.Ceiling(120 * options.Scale),art.Height);
            Assert.Equal(16 * options.Scale, art.HotspotX);
            Assert.Equal(16 * options.Scale, art.HotspotY);
            var pixels=art.Pixels.ToArray();
            Assert.Equal(art.Width*art.Height*4,pixels.Length);
            var occupied=0;
            for(var y=0;y<art.Height;y++)
            for(var x=0;x<art.Width;x++)
            {
                var alpha=pixels[(y*art.Width+x)*4+3];
                if(alpha==0)continue;
                occupied++;
                Assert.InRange(x,1,art.Width-2);
                Assert.InRange(y,1,art.Height-2);
            }
            Assert.True(occupied>100);
            var tipRegion=false;
            for(var y=(int)art.HotspotY-2;y<art.HotspotY+5;y++)
            for(var x=(int)art.HotspotX-2;x<art.HotspotX+5;x++)
                tipRegion |= pixels[(y*art.Width+x)*4+3]>0;
            Assert.True(tipRegion, "The leading tip must stay at the anchor.");
            if(size==TeachingPointerSize.Standard)
                fingerprints.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
        }
        Assert.Equal(4,fingerprints.Count);
    }

    [Fact]
    public void WarmAndTealHeadsHaveTheirApprovedDistinctColors()
    {
        var warm=TeachingPointerArtwork.Get(new()).Pixels.Span;
        var teal=TeachingPointerArtwork.Get(new(TeachingPointerStyle.TealSilver)).Pixels.Span;
        var i=(21*140+23)*4;
        Assert.True(warm[i]>150 && warm[i+1]<180 && warm[i+2]<120);
        Assert.True(teal[i]<100 && teal[i+1]>120 && teal[i+2]>120);
    }
}
