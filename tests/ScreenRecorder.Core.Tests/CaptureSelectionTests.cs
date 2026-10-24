// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Tests;

public class CaptureSelectionTests
{
    [Fact]
    public void MissingModeDefaultsToAutomaticWithoutReinterpretingStoredManualChoices()
    {
        Assert.Equal((WindowsCaptureMode)2, JsonSerializer.Deserialize<UserSettings>("{}")!.WindowsCaptureMode);
        Assert.Equal((WindowsCaptureMode)2, JsonSerializer.Deserialize<RecordingConfiguration>("{}")!.WindowsCaptureMode);
        Assert.Equal(WindowsCaptureMode.CompatibleGdi, JsonSerializer.Deserialize<UserSettings>("{\"WindowsCaptureMode\":0}")!.WindowsCaptureMode);
        Assert.Equal(WindowsCaptureMode.ModernExperimental, JsonSerializer.Deserialize<UserSettings>("{\"WindowsCaptureMode\":1}")!.WindowsCaptureMode);
        Assert.Equal((WindowsCaptureMode)2, JsonSerializer.Deserialize<UserSettings>("{\"WindowsCaptureMode\":2}")!.WindowsCaptureMode);
        Assert.Equal((WindowsCaptureMode)2, JsonSerializer.Deserialize<RecordingConfiguration>("{\"WindowsCaptureMode\":2}")!.WindowsCaptureMode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void UnknownModeNormalizesToGdi(int value)
    {
        var json = "{\"WindowsCaptureMode\":" + value + "}";
        Assert.Equal(WindowsCaptureMode.CompatibleGdi, JsonSerializer.Deserialize<UserSettings>(json)!.WindowsCaptureMode);
        Assert.Equal(WindowsCaptureMode.CompatibleGdi, JsonSerializer.Deserialize<RecordingConfiguration>(json)!.WindowsCaptureMode);
    }

    [Fact]
    public void PausedSettingsCannotChangeCaptureMode()
    {
        var original = new RecordingConfiguration { WindowsCaptureMode = WindowsCaptureMode.ModernExperimental };
        original.ApplyPausedSettings(new() { WindowsCaptureMode = WindowsCaptureMode.CompatibleGdi,
            AudioSource = AudioSourceType.MicrophoneOnly, CursorEffect = CursorEffectMode.Hidden });
        Assert.Equal(WindowsCaptureMode.ModernExperimental, original.WindowsCaptureMode);
        Assert.Equal(AudioSourceType.MicrophoneOnly, original.AudioSource);
        Assert.Equal(CursorEffectMode.Hidden, original.CursorEffect);
    }

    [Fact]
    public void OldSessionWithoutCaptureSelectionLoads()
    {
        Assert.Null(JsonSerializer.Deserialize<RecordingSession>("{}")!.CaptureSelection);
        Assert.Null(JsonSerializer.Deserialize<RecorderTelemetry>("{}")!.CaptureSelection);
    }
}
