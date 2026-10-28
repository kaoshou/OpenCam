// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public enum ProjectVideoImportState { Running, Succeeded, Canceled, Failed }
public sealed record ProjectVideoImportStatus(Guid Id, ProjectVideoImportState State, string Phase,
    string Name, string? Error = null, Guid? ClipId = null);
