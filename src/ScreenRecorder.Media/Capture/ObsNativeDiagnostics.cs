// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace ScreenRecorder.Media.Capture;

internal sealed record ObsStartupDiagnostics(string Stage, long ElapsedMs, long SourceWidth, long SourceHeight,
    long ExpectedWidth, long ExpectedHeight, bool SystemSelected, bool MicrophoneSelected,
    bool SystemSamples, bool MicrophoneSamples, long Frames, long Bytes)
{
    public static ObsStartupDiagnostics? Read(JsonElement value)
    {
        if (!value.TryGetProperty("startup", out var data)) return null; // Protocol 1 additions are optional.
        var stage = data.GetProperty("stage").GetString();
        if (stage is not ("initializing" or "core_startup" or "graphics" or "modules" or "capture_source" or
            "audio_sources" or "source_wait" or "encoder_start" or "first_frame" or "recording" or "stopping")) throw new JsonException();
        return new(stage, Number(data,"elapsedMs",86400000), Number(data,"sourceWidth",16384),
            Number(data,"sourceHeight",16384), Number(data,"expectedWidth",16384), Number(data,"expectedHeight",16384),
            data.GetProperty("systemSelected").GetBoolean(), data.GetProperty("microphoneSelected").GetBoolean(),
            data.GetProperty("systemSamples").GetBoolean(), data.GetProperty("microphoneSamples").GetBoolean(),
            Number(data,"frames",long.MaxValue), Number(data,"bytes",long.MaxValue));
    }

    internal static long Number(JsonElement data, string key, long maximum)
    {
        var number = data.GetProperty(key).GetInt64();
        return number >= 0 && number <= maximum ? number : throw new JsonException();
    }

    public static string SafeCode(string? code) => code is "invalid_arguments" or "invalid_request" or "runtime_path" or
        "invalid_audio" or "invalid_encoder" or "core_startup" or "core_version" or "graphics_startup" or "audio_startup" or
        "runtime_module" or "capture_source" or "display_identity" or "audio_device_identity" or "encoder_unavailable" or
        "audio_encoder" or "mux_create" or "capture_cancelled" or "capture_start_timeout" or "encoder_start" or
        "capture_source_lost" or "recording_output" or "capture_geometry_lost" or "audio_device_lost" or "audio_keepalive_init" or "mux_finalize"
        ? code : "native_failure";
}

// OBS render timing is not a measure of unique frames delivered by the screen source.
internal sealed record ObsRenderDiagnostics(long RenderFrames, long LaggedFrames, long AverageRenderNs)
{
    public static ObsRenderDiagnostics? Read(JsonElement value)
    {
        if (!value.TryGetProperty("renderFrames", out _)) return null;
        var frames = ObsStartupDiagnostics.Number(value,"renderFrames",uint.MaxValue);
        return new(frames, ObsStartupDiagnostics.Number(value,"laggedFrames",frames),
            ObsStartupDiagnostics.Number(value,"averageRenderNs",1000000000000));
    }
}
