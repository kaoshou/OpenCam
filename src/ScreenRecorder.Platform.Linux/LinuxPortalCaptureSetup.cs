// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Platform.Linux;

internal sealed class LinuxPortalCaptureSetup(LinuxPortalSources sources) : IObsCaptureSetup
{
    private LinuxPortalSources.RestoreGrant? grant;
    private bool accepted;
    public byte[] SerializeRequest(ObsCaptureRequest request)
    {
        if (grant is not null) throw new IOException("Wayland setup cannot be reused.");
        grant = sources.BeginRestore(request.Monitor, request.MonitorWidth, request.MonitorHeight);
        var json = JsonNode.Parse(request.Serialize())!.AsObject();
        json["portalRestoreToken"] = grant.Token;
        json["portalSourceId"] = request.Monitor;
        return JsonSerializer.SerializeToUtf8Bytes(json);
    }
    public void AcceptGrant(JsonElement value)
    {
        if (grant is null || accepted || value.GetProperty("backend").GetString() != "pipewire") throw new JsonException();
        grant.Rotate(value.GetProperty("sourceId").GetString() ?? "", value.GetProperty("width").GetInt32(),
            value.GetProperty("height").GetInt32(), value.GetProperty("restoreToken").GetString() ?? "");
        accepted = true;
    }
    public void ValidateReady() { if (!accepted) throw new JsonException(); }
    public void Dispose() => grant?.Dispose();
    public override string ToString() => "Wayland capture setup (redacted)";
}
