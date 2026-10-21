// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Projects;

public enum ProjectExportFormat { Mp4 = 0, Gif = 1 }
public enum ProjectExportScope { WholeProject = 0, Clip = 1 }
public sealed record GifExportOptions(int? MaximumWidth = 640, int FramesPerSecond = 15);
public sealed record ProjectExportOptions(ProjectExportFormat Format = ProjectExportFormat.Mp4,
    ProjectExportScope Scope = ProjectExportScope.WholeProject, Guid? ClipId = null, GifExportOptions? Gif = null);
