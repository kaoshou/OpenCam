// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.Windows.Audio;

namespace ScreenRecorder.Media.Tests;

public sealed class WindowsMicrophoneEndpointMatcherTests
{
    private static readonly AudioDeviceOption[] Dshow =
    [
        new("@device_pnp_mic", "Built-in Microphone", true, true)
    ];

    [Fact]
    public void UniqueExactName_AndAlternativeId_ResolveSameEndpoint()
    {
        var endpoints = new[] { ("endpoint-1", "Built-in Microphone") };

        Assert.Equal("endpoint-1", WindowsMicrophoneEndpointMatcher.Match(
            "Built-in Microphone", Dshow, endpoints));
        Assert.Equal("endpoint-1", WindowsMicrophoneEndpointMatcher.Match(
            "@device_pnp_mic", Dshow, endpoints));
    }

    [Fact]
    public void DuplicateOrMissingNames_DoNotGuess()
    {
        var duplicate = new[]
        {
            ("endpoint-1", "Built-in Microphone"),
            ("endpoint-2", "Built-in Microphone")
        };
        Assert.Null(WindowsMicrophoneEndpointMatcher.Match("@device_pnp_mic", Dshow, duplicate));
        Assert.Null(WindowsMicrophoneEndpointMatcher.Match("unknown", Dshow, duplicate));
        Assert.Null(WindowsMicrophoneEndpointMatcher.Match(
            "@device_pnp_mic", Dshow, new[] { ("endpoint-1", "External Microphone") }));
    }

    [Fact]
    public void DefaultMicrophoneOption_RemainsUnavailableEvenWithOneEndpoint()
    {
        var options = new[] { new AudioDeviceOption("default_mic", "系統預設麥克風 (Default Microphone)", true, true) };
        Assert.Null(WindowsMicrophoneEndpointMatcher.Match(
            "default_mic", options, new[] { ("endpoint-1", "Built-in Microphone") }));
    }
}
