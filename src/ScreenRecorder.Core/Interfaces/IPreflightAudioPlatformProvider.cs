// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Interfaces;

public interface IPreflightAudioPlatformProvider
{
    string BuildPreflightAudioInputArguments(RecordingConfiguration configuration, bool synthetic,
        string? systemAudioInput, string? microphoneAudioInput);
}
