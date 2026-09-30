// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Enums;

public enum WindowsCaptureMode { CompatibleGdi = 0, ModernExperimental = 1 }
public enum CaptureBackend { PlatformDefault = 0, Gdi = 1, DesktopDuplication = 2 }
public enum CaptureFallbackReason { None, FilterUnavailable, MappingUncertain, UnsupportedTopology, StartupFailed }
