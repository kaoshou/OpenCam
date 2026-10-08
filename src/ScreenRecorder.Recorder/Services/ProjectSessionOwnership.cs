// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Recorder.Services;

// Internal capability: no ordinary recording/IPC configuration can set this policy.
internal sealed record ProjectSessionOwnership(IProjectHandle Handle);
