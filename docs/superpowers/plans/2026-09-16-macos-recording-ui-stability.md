# macOS Recording and UI Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make selected-display recording start reliably on macOS, keep the custom-region interior transparent, and make the output-folder action work in Finder without changing Windows behavior.

**Architecture:** Keep the existing AVFoundation and FFmpeg pipeline, but make the VideoToolbox pixel-format contract explicit and replace the fixed startup sleep with a first-frame handshake. Isolate Finder/Explorer command construction and macOS selector appearance in small testable helpers.

**Tech Stack:** .NET 8, C#, FFmpeg/AVFoundation, Apple VideoToolbox, Avalonia 11, xUnit, Serilog

**Spec:** `docs/superpowers/specs/2026-09-16-macos-audio-stability-and-app-integration-design.md`

## Global Constraints

- Preserve existing Windows FFmpeg, Explorer, selector, and encoder behavior.
- Keep MKV as the working container and the existing MP4 remux flow.
- The UI must not enter Recording until FFmpeg emits its first valid progress frame.
- The macOS selection interior must be transparent; the border, handles, and toolbar remain visible.
- Process arguments must use `ProcessStartInfo.ArgumentList`; do not invoke a shell.
- Every behavior change follows red-green TDD and ends in a focused commit.

---

## File Map

- `src/ScreenRecorder.Platform.macOS/MacOsFFmpegProvider.cs`: macOS video encoder pixel-format arguments.
- `src/ScreenRecorder.Media/Capture/FFmpegScreenRecorderEngine.cs`: asynchronous first-frame startup handshake and stderr tail.
- `src/ScreenRecorder.UI/Views/RegionSelectorAppearance.cs`: platform-specific transparency policy.
- `src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml`: transparent selection surface.
- `src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml.cs`: applies the appearance policy.
- `src/ScreenRecorder.UI/Services/PlatformFolderOpener.cs`: safe Finder/Explorer process construction and launch.
- `src/ScreenRecorder.UI/ViewModels/MainViewModel.cs`: delegates folder opening and presents failures.
- `src/ScreenRecorder.Core/Localization/LocalizationService.cs`: localized folder-open failure message.
- `tests/ScreenRecorder.Media.Tests/MacOsFFmpegProviderTests.cs`: VideoToolbox output regression tests.
- `tests/ScreenRecorder.Media.Tests/FFmpegStartupHandshakeTests.cs`: delayed FFmpeg failure and first-frame tests.
- `tests/ScreenRecorder.Media.Tests/RegionSelectorAppearanceTests.cs`: macOS transparency policy tests.
- `tests/ScreenRecorder.Media.Tests/PlatformFolderOpenerTests.cs`: Finder/Explorer command tests.
- `tests/ScreenRecorder.Media.Tests/PlatformTestAttributes.cs`: reusable Unix/macOS test attribute.

### Task 1: Make the VideoToolbox pixel format explicit

**Files:**
- Modify: `tests/ScreenRecorder.Media.Tests/MacOsFFmpegProviderTests.cs`
- Modify: `src/ScreenRecorder.Platform.macOS/MacOsFFmpegProvider.cs`

**Interfaces:**
- Consumes: `IFFmpegPlatformProvider.BuildOutputArguments(RecordingConfiguration, HardwareEncoderType, string)`.
- Produces: VideoToolbox output containing `-pix_fmt nv12`; software output containing `-pix_fmt yuv420p`.

- [ ] **Step 1: Write failing output-argument tests**

Add these tests to `MacOsFFmpegProviderTests`:

```csharp
[Fact]
public void VideoToolboxOutput_UsesNv12PixelFormat()
{
    var args = CreateProvider().BuildOutputArguments(
        new RecordingConfiguration { VideoBitrateKbps = 6000 },
        HardwareEncoderType.AppleVideoToolbox,
        "/tmp/output.mkv");

    Assert.Contains("-c:v h264_videotoolbox", args);
    Assert.Contains("-pix_fmt nv12", args);
}

[Fact]
public void SoftwareOutput_UsesYuv420pPixelFormat()
{
    var args = CreateProvider().BuildOutputArguments(
        new RecordingConfiguration { VideoBitrateKbps = 6000 },
        HardwareEncoderType.SoftwareCpu,
        "/tmp/output.mkv");

    Assert.Contains("-c:v libx264", args);
    Assert.Contains("-pix_fmt yuv420p", args);
}
```

- [ ] **Step 2: Run the focused tests and observe failure**

Run:

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~MacOsFFmpegProviderTests'
```

Expected: both new tests fail because actual recording output does not declare either pixel format.

- [ ] **Step 3: Add encoder-specific pixel formats**

Update `BuildOutputArguments` so the codec branch also adds its format:

```csharp
if (videoCodec == "h264_videotoolbox")
{
    args.Add("-profile:v main");
    args.Add("-pix_fmt nv12");
    args.Add($"-b:v {config.VideoBitrateKbps}k");
}
else
{
    args.Add("-preset fast");
    args.Add("-pix_fmt yuv420p");
    args.Add($"-b:v {config.VideoBitrateKbps}k");
}
```

- [ ] **Step 4: Run the provider tests**

Run the command from Step 2.

Expected: all `MacOsFFmpegProviderTests` pass, including full-display no-crop and relative crop tests.

- [ ] **Step 5: Commit**

```bash
git add src/ScreenRecorder.Platform.macOS/MacOsFFmpegProvider.cs tests/ScreenRecorder.Media.Tests/MacOsFFmpegProviderTests.cs
git commit -m "fix: normalize macOS encoder pixel formats"
```

### Task 2: Replace the 350 ms startup guess with a first-frame handshake

**Files:**
- Modify: `tests/ScreenRecorder.Media.Tests/PlatformTestAttributes.cs`
- Create: `tests/ScreenRecorder.Media.Tests/FFmpegStartupHandshakeTests.cs`
- Modify: `src/ScreenRecorder.Media/Capture/FFmpegScreenRecorderEngine.cs`

**Interfaces:**
- Consumes: `FFmpegScreenRecorderEngine.StartRecordingAsync(...)` and injectable `ffmpegPath` constructor argument.
- Produces: `LaunchProcessAsync(...) : Task<bool>`, internal first-frame signal, and a bounded stderr tail used in failures.

- [ ] **Step 1: Add a Unix-only test attribute**

Append to `PlatformTestAttributes.cs`:

```csharp
internal sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Requires a Unix executable test fixture.";
        }
    }
}
```

- [ ] **Step 2: Write delayed-failure and first-frame tests**

Create `FFmpegStartupHandshakeTests.cs` with a temporary executable fixture using `File.SetUnixFileMode`. The two executable bodies must be:

```csharp
private static string CreateExecutable(string body)
{
    var path = Path.Combine(Path.GetTempPath(), "opencam-ffmpeg-" + Guid.NewGuid().ToString("N"));
    File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
    File.SetUnixFileMode(path,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    return path;
}
```

```csharp
[UnixOnlyFact]
public async Task DelayedExitBeforeFirstFrame_IsRejected()
{
    var executable = CreateExecutable("sleep 0.6\necho 'Error while opening encoder' >&2\nexit 1");
    await using var engine = new FFmpegScreenRecorderEngine(
        new StubProvider(), null, executable);

    var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        engine.StartRecordingAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"),
            Config(), new CaptureRegion(0, 0, 640, 480)));

    Assert.Contains("初始化失敗", error.Message);
    File.Delete(executable);
}

[UnixOnlyFact]
public async Task FirstFrame_CompletesStartupHandshake()
{
    var executable = CreateExecutable(
        "echo 'frame=    1 fps=0.0 time=00:00:00.03' >&2\n" +
        "while IFS= read -r line; do [ \"$line\" = q ] && exit 0; done");
    await using var engine = new FFmpegScreenRecorderEngine(
        new StubProvider(), null, executable);

    await engine.StartRecordingAsync(
        Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"),
        Config(), new CaptureRegion(0, 0, 640, 480));

    Assert.True(engine.IsRunning);
    await engine.StopRecordingAsync();
    File.Delete(executable);
}
```

Define `Config()` with `EncoderType = HardwareEncoderType.SoftwareCpu`, `AudioSource = AudioSourceType.None`, and define `StubProvider` to return an empty input string plus `"-f null -"` output. This prevents hardware probing and lets the fixture ignore all arguments.

- [ ] **Step 3: Run the new tests and observe the delayed failure being accepted**

Run:

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~FFmpegStartupHandshakeTests'
```

Expected: `DelayedExitBeforeFirstFrame_IsRejected` fails because the current engine returns after 350 ms; the first-frame test may pass accidentally and does not count as completion until both results are correct.

- [ ] **Step 4: Add handshake state and bounded stderr capture**

In `FFmpegScreenRecorderEngine`, add:

```csharp
private TaskCompletionSource<bool>? _startupSignal;
private readonly Queue<string> _stderrTail = new();
private const int StderrTailLimit = 40;
private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
```

Change `LaunchProcess` to `LaunchProcessAsync` and start `ReadStderrLoop` immediately after assigning `_process`. Initialize `_startupSignal` with `TaskCreationOptions.RunContinuationsAsynchronously`. Wait for the first of the startup signal, process exit, or five-second timeout:

```csharp
var exitTask = proc.WaitForExitAsync(cancellationToken);
var timeoutTask = Task.Delay(StartupTimeout, cancellationToken);
var completed = await Task.WhenAny(_startupSignal.Task, exitTask, timeoutTask);

if (completed == _startupSignal.Task && await _startupSignal.Task)
{
    return true;
}

var reason = string.Join(Environment.NewLine, _stderrTail);
Log.Warning("FFmpeg 未在期限內產生第一個影格: {Reason}", reason);
try { if (!proc.HasExited) proc.Kill(true); } catch { }
try { await exitTask; } catch { }
return false;
```

In `ReadStderrLoop`, enqueue each line while holding `_lock`, remove older lines past 40, and set `_startupSignal.TrySetResult(true)` when either `FrameRegex` or `TimeRegex` matches. At clean or failed EOF before a progress match, call `_startupSignal.TrySetResult(false)`.

Update `StartRecordingAsync` to await the hardware attempt and, when necessary, await exactly one software attempt. Do not hold `_lock` across either await.

- [ ] **Step 5: Run handshake and provider tests**

Run:

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~FFmpegStartupHandshakeTests|FullyQualifiedName~MacOsFFmpegProviderTests'
```

Expected: all selected tests pass; the delayed failure takes at least 0.6 seconds and is not reported as running.

- [ ] **Step 6: Commit**

```bash
git add src/ScreenRecorder.Media/Capture/FFmpegScreenRecorderEngine.cs tests/ScreenRecorder.Media.Tests/PlatformTestAttributes.cs tests/ScreenRecorder.Media.Tests/FFmpegStartupHandshakeTests.cs
git commit -m "fix: wait for first frame before reporting recording"
```

### Task 3: Make the macOS region interior transparent

**Files:**
- Create: `src/ScreenRecorder.UI/Views/RegionSelectorAppearance.cs`
- Modify: `src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml`
- Modify: `src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml.cs`
- Create: `tests/ScreenRecorder.Media.Tests/RegionSelectorAppearanceTests.cs`

**Interfaces:**
- Produces: `RegionSelectorAppearance.TransparencyLevels(bool isMacOS)` and `RegionSelectorAppearance.FallbackColor(bool isMacOS)`.
- Consumes: Avalonia `WindowTransparencyLevel` and `Color`.

- [ ] **Step 1: Write the transparency policy test**

```csharp
using Avalonia.Controls;
using Avalonia.Media;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public class RegionSelectorAppearanceTests
{
    [Fact]
    public void MacOs_UsesTransparentCompositionWithoutBlur()
    {
        var levels = RegionSelectorAppearance.TransparencyLevels(isMacOS: true);

        Assert.Equal(new[] { WindowTransparencyLevel.Transparent }, levels);
        Assert.Equal(Colors.Transparent, RegionSelectorAppearance.FallbackColor(isMacOS: true));
        Assert.DoesNotContain(WindowTransparencyLevel.Blur, levels);
    }
}
```

- [ ] **Step 2: Run the test and observe the missing type**

Run:

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~RegionSelectorAppearanceTests'
```

Expected: compilation fails because `RegionSelectorAppearance` does not exist.

- [ ] **Step 3: Implement and apply the policy**

Create `RegionSelectorAppearance.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Media;

namespace ScreenRecorder.UI.Views;

internal static class RegionSelectorAppearance
{
    internal static IReadOnlyList<WindowTransparencyLevel> TransparencyLevels(bool isMacOS) =>
        isMacOS
            ? new[] { WindowTransparencyLevel.Transparent }
            : new[] { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None };

    internal static Color FallbackColor(bool isMacOS) => Colors.Transparent;
}
```

In `RegionSelectWindow.axaml.cs`, replace the macOS Blur/fallback block with values from this helper. In `RegionSelectWindow.axaml`, change the outer selection `Border` from `Background="#182ECC71"` to `Background="Transparent"`. Keep `BorderBrush="#2ECC71"`, `BorderThickness="4"`, the toolbar, and all eight resize handles unchanged.

- [ ] **Step 4: Run the test and build the UI**

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~RegionSelectorAppearanceTests'
dotnet build src/ScreenRecorder.UI/ScreenRecorder.UI.csproj -c Release
```

Expected: test passes and UI build succeeds with zero errors.

- [ ] **Step 5: Commit**

```bash
git add src/ScreenRecorder.UI/Views/RegionSelectorAppearance.cs src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml.cs tests/ScreenRecorder.Media.Tests/RegionSelectorAppearanceTests.cs
git commit -m "fix: keep macOS region selector interior transparent"
```

### Task 4: Open recordings in Finder and preserve Explorer behavior

**Files:**
- Create: `src/ScreenRecorder.UI/Services/PlatformFolderOpener.cs`
- Modify: `src/ScreenRecorder.UI/ViewModels/MainViewModel.cs`
- Modify: `src/ScreenRecorder.Core/Localization/LocalizationService.cs`
- Create: `tests/ScreenRecorder.Media.Tests/PlatformFolderOpenerTests.cs`
- Modify: `tests/ScreenRecorder.Core.Tests/LocalizationTests.cs`

**Interfaces:**
- Produces: `PlatformFolderOpener.BuildStartInfo(bool isWindows, bool isMacOS, string? lastOutputFilePath, string outputDirectory) : ProcessStartInfo?`.
- Produces: `PlatformFolderOpener.TryOpen(...) : (bool Success, string? Error)`.
- Consumes: `MainViewModel.LastOutputFilePath` and `OutputDirectory`.

- [ ] **Step 1: Write Finder and Explorer command tests**

Create `PlatformFolderOpenerTests.cs`:

```csharp
using ScreenRecorder.UI.Services;

namespace ScreenRecorder.Media.Tests;

public class PlatformFolderOpenerTests
{
    [Fact]
    public void MacFile_UsesFinderRevealWithOnePathArgument()
    {
        var info = PlatformFolderOpener.BuildStartInfo(
            false, true, "/tmp/My Recording.mp4", "/tmp");

        Assert.Equal("/usr/bin/open", info!.FileName);
        Assert.Equal(new[] { "-R", "/tmp/My Recording.mp4" }, info.ArgumentList);
        Assert.False(info.UseShellExecute);
    }

    [Fact]
    public void MacDirectory_UsesOpen()
    {
        var info = PlatformFolderOpener.BuildStartInfo(false, true, null, "/tmp/My Recordings");
        Assert.Equal(new[] { "/tmp/My Recordings" }, info!.ArgumentList);
    }

    [Fact]
    public void WindowsFile_PreservesExplorerSelection()
    {
        var info = PlatformFolderOpener.BuildStartInfo(
            true, false, @"C:\Recordings\clip.mp4", @"C:\Recordings");

        Assert.Equal("explorer.exe", info!.FileName);
        Assert.Equal(new[] { "/select,C:\\Recordings\\clip.mp4" }, info.ArgumentList);
    }
}
```

The helper will use injectable booleans only for deterministic tests; `TryOpen` supplies `OperatingSystem.IsWindows()` and `OperatingSystem.IsMacOS()`.

- [ ] **Step 2: Run the tests and observe the missing helper**

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~PlatformFolderOpenerTests'
```

Expected: compilation fails because the helper does not exist.

- [ ] **Step 3: Implement safe process construction**

Create `PlatformFolderOpener.cs` with this decision order:

```csharp
internal static ProcessStartInfo? BuildStartInfo(
    bool isWindows,
    bool isMacOS,
    string? lastOutputFilePath,
    string outputDirectory)
{
    var hasFile = !string.IsNullOrWhiteSpace(lastOutputFilePath) && File.Exists(lastOutputFilePath);
    var target = hasFile ? lastOutputFilePath! : outputDirectory;
    if (!hasFile && !Directory.Exists(target)) return null;

    var info = new ProcessStartInfo { UseShellExecute = false };
    if (isMacOS)
    {
        info.FileName = "/usr/bin/open";
        if (hasFile) info.ArgumentList.Add("-R");
        info.ArgumentList.Add(target);
        return info;
    }

    if (isWindows)
    {
        info.FileName = "explorer.exe";
        info.ArgumentList.Add(hasFile ? "/select," + target : target);
        return info;
    }

    return null;
}
```

`TryOpen` builds the info, returns a localized-neutral error detail when no valid target or platform exists, starts the process, and logs exceptions with Serilog. It must not catch without logging.

- [ ] **Step 4: Connect the view model and localization**

Replace `MainViewModel.OpenOutputFolder`'s direct `explorer.exe` calls with `PlatformFolderOpener.TryOpen(LastOutputFilePath, OutputDirectory)`. On failure set:

```csharp
StatusMessage = Strings.GetFormatted("StatusOpenFolderFailed", error ?? string.Empty);
```

Add both localized values to `LocalizationService.ZhTwDictionary` and `LocalizationService.EnUsDictionary`:

```text
zh-TW: 無法開啟儲存位置：{0}
en-US: Unable to open the output location: {0}
```

Extend `LocalizationTests` to assert `StatusOpenFolderFailed` exists in both languages.

- [ ] **Step 5: Run focused tests**

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~PlatformFolderOpenerTests'
dotnet test tests/ScreenRecorder.Core.Tests/ScreenRecorder.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~LocalizationTests'
```

Expected: both suites pass.

- [ ] **Step 6: Commit**

```bash
git add src/ScreenRecorder.UI/Services/PlatformFolderOpener.cs src/ScreenRecorder.UI/ViewModels/MainViewModel.cs src/ScreenRecorder.Core/Localization/LocalizationService.cs tests/ScreenRecorder.Media.Tests/PlatformFolderOpenerTests.cs tests/ScreenRecorder.Core.Tests/LocalizationTests.cs
git commit -m "fix: open recording locations with the native file manager"
```

### Task 5: Verify the recording/UI phase

**Files:**
- No production changes expected.

**Interfaces:**
- Consumes: all outputs from Tasks 1–4.
- Produces: a green automated baseline before native audio work starts.

- [ ] **Step 1: Run all automated tests**

```bash
dotnet test tests/ScreenRecorder.Core.Tests/ScreenRecorder.Core.Tests.csproj -c Release
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release
```

Expected: zero failures; Windows hardware tests are skipped on macOS.

- [ ] **Step 2: Build the full solution**

```bash
dotnet build ScreenRecorder.sln -c Release
```

Expected: zero build errors and no new warnings.

- [ ] **Step 3: Inspect the Windows provider diff**

```bash
git diff HEAD~4 -- src/ScreenRecorder.Platform.Windows src/ScreenRecorder.Platform.Windows/Audio src/ScreenRecorder.UI/Assets/app_icon.ico
```

Expected: no Windows provider, WASAPI, or icon changes.

- [ ] **Step 4: Record verification evidence**

Append the exact test/build command results to the implementation handoff. Do not create a success commit when no files changed.
