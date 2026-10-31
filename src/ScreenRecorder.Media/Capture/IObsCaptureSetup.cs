// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace ScreenRecorder.Media.Capture;

// Optional per-segment private setup. Existing platforms leave this null and
// retain the original request bytes and strict event handling.
public interface IObsCaptureSetup : IDisposable
{
    byte[] SerializeRequest(ObsCaptureRequest request);
    void AcceptGrant(JsonElement grant);
    void ValidateReady();
}
