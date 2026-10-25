// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.UI.ViewModels;

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
Check(typeof(ObsRuntime).Assembly.GetType("ScreenRecorder.Media.Capture.FFmpegScreenRecorderEngine") is null,
    "Native-only build still contains the legacy screen recorder.");
var profile = typeof(RecordingConfiguration).Assembly.GetType("ScreenRecorder.Core.Models.RecordingBuildProfile");
Check(profile?.GetProperty("NativeOnlyTest")?.GetValue(null) is true,"Native-only build profile is not active.");
Check(new UserSettings().Fps == 20 && new RecordingConfiguration().Fps == 20,"First launch must default to 20 FPS.");
try
{
    _ = new RecordingEngineFactory(null!,null!).Create(HardwareEncoderType.SoftwareCpu);
    throw new InvalidOperationException("Missing runtime did not fail closed.");
}
catch (IOException) { }
Check(typeof(ObsRuntime).Assembly.GetType("ScreenRecorder.Media.Preflight.FFmpegPreflightAudioCapture") is not null,
    "Independent audio preflight must remain available.");
foreach (var previousMode in Enum.GetValues<WindowsCaptureMode>())
{
    var settings = new SettingsFixture { Settings = new() { WindowsCaptureMode = previousMode } };
    var model = new SettingsViewModel(settings, supportsWindowsCapture: true);
    await model.LoadSettingsAsync();
    Check(model.AvailableCaptureModes.Count == 1 && model.SelectedCaptureMode?.Mode == WindowsCaptureMode.Automatic,
        "Old capture preferences must not expose a legacy capture option.");
    await model.SaveAsync();
    Check(settings.Settings.WindowsCaptureMode == WindowsCaptureMode.Automatic,"Saving settings re-enabled legacy capture.");
}
if (args.Length == 1)
{
    var root = Path.GetFullPath(args[0]);
    Check(!File.Exists(Path.Combine(root,"OpenCam.ScreenCapture.exe")),"Legacy native helper is still packaged.");
    Check(ObsRuntime.TryLoad(Path.Combine(root,"obs-runtime")) is not null,"Packaged native runtime is not accepted.");
}
Console.WriteLine("PASS: legacy screen-recorder type absent; native-only profile active; missing runtime fails closed.");

sealed class SettingsFixture : ISettingsService
{
    public UserSettings Settings { get; set; } = new();
    public Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);
    public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
    { Settings = settings; return Task.CompletedTask; }
}
