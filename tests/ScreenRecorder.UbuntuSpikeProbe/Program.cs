// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Recorder.Services;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Run this probe on Ubuntu, not the build host.");
var output = args.Single();
var blocked = false;
string? reason = null;
try { _ = new RecordingEngineFactory(null!, null!).Create(HardwareEncoderType.SoftwareCpu); }
catch (IOException error) { blocked = true; reason = error.Message; }
string? displayError = null;
try { _ = new MacOsDisplayService().GetMonitors(); }
catch (Exception error) { displayError = error.GetType().Name + ": " + error.Message; }
var legacyAbsent = typeof(ObsRuntime).Assembly.GetType("ScreenRecorder.Media.Capture.FFmpegScreenRecorderEngine") is null;
var report = new
{
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    framework = RuntimeInformation.FrameworkDescription,
    nativeOnly = RecordingBuildProfile.NativeOnlyTest,
    recognizedObsPlatform = ObsRuntime.CurrentPlatform,
    legacyRecordingCoreAbsent = legacyAbsent,
    recordingFactoryRejected = blocked,
    recordingRejection = reason,
    selectedMacDisplayProviderError = displayError,
    recordingStatus = "NOT_SUPPORTED",
    physicalScreenAndAudioAcceptance = "REQUIRES MANUAL VALIDATION AFTER LINUX IMPLEMENTATION",
    note = "A successful probe verifies safe rejection, not functioning recording."
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(output, json + "\n");
Console.WriteLine(json);
return RecordingBuildProfile.NativeOnlyTest && legacyAbsent && blocked && ObsRuntime.CurrentPlatform is null && displayError is not null ? 0 : 1;
