# macOS Native System Audio and Microphone Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add driver-free macOS system-audio capture and stable built-in microphone recording, including system-only, microphone-only, and mixed modes.

**Architecture:** A bundled Swift helper captures system audio with ScreenCaptureKit and writes fixed 48 kHz stereo `s16le` PCM to a private FIFO. FFmpeg keeps AVFoundation for screen and microphone capture, places microphone input on a separate queue, resamples timestamps asynchronously, and mixes sources when both are enabled.

**Tech Stack:** macOS 13+, Swift 6, ScreenCaptureKit, AVFoundation, CoreMedia, .NET 8, C#, FFmpeg, xUnit

**Spec:** `docs/superpowers/specs/2026-09-16-macos-audio-stability-and-app-integration-design.md`

## Global Constraints

- Minimum supported macOS version is 13.0.
- End users must not install BlackHole, Soundflower, or another audio driver.
- System PCM contract is signed 16-bit little-endian, 48,000 Hz, two channels.
- The helper excludes OpenCam's own process audio.
- Windows continues to use the existing WASAPI loopback implementation.
- Video-only recording remains available if system-audio startup fails.
- Temporary FIFO paths are private, per recording, and removed on every stop/failure path.
- Every behavior change follows red-green TDD and ends in a focused commit.

---

## File Map

- `src/ScreenRecorder.Platform.macOS/Native/OpenCamSystemAudio/main.swift`: ScreenCaptureKit capture executable.
- `src/ScreenRecorder.Platform.macOS/MacOsSystemAudioSupport.cs`: helper discovery and macOS-version capability.
- `src/ScreenRecorder.Platform.macOS/UnixFifo.cs`: `mkfifo(2)` wrapper with mode `0600`.
- `src/ScreenRecorder.Platform.macOS/MacOsAudioLoopbackCapture.cs`: helper/FIFO lifecycle and FFmpeg PCM contract.
- `src/ScreenRecorder.Platform.macOS/MacOsDisplayService.cs`: resolves monitor index to CoreGraphics display ID.
- `src/ScreenRecorder.Platform.macOS/MacOsFFmpegProvider.cs`: separate AVFoundation microphone input and audio filters/maps.
- `src/ScreenRecorder.Core/Interfaces/ISystemAudioLoopbackCapture.cs`: passes selected monitor index into system-audio startup.
- `src/ScreenRecorder.Media/Capture/FFmpegScreenRecorderEngine.cs`: starts system audio with selected monitor and logs platform-neutral language.
- `src/ScreenRecorder.Platform.Windows/Audio/WindowsWasapiLoopbackCapture.cs`: accepts and ignores monitor index.
- `src/ScreenRecorder.UI/ViewModels/MainViewModel.cs`: macOS system-audio capability and configuration.
- `src/ScreenRecorder.Core/Localization/LocalizationService.cs`: system-audio labels/errors.
- `tests/ScreenRecorder.Media.Tests/MacOsSystemAudioSupportTests.cs`: version/helper capability.
- `tests/ScreenRecorder.Media.Tests/MacOsAudioLoopbackCaptureTests.cs`: FIFO/helper lifecycle.
- `tests/ScreenRecorder.Media.Tests/MacOsFFmpegProviderTests.cs`: four audio-mode argument snapshots.
- `tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs`: capability-to-audio-mode behavior.

### Task 1: Pass the selected display into the system-audio contract

**Files:**
- Modify: `src/ScreenRecorder.Core/Interfaces/ISystemAudioLoopbackCapture.cs`
- Modify: `src/ScreenRecorder.Media/Capture/FFmpegScreenRecorderEngine.cs`
- Modify: `src/ScreenRecorder.Platform.Windows/Audio/WindowsWasapiLoopbackCapture.cs`
- Modify: `src/ScreenRecorder.Platform.macOS/MacOsAudioLoopbackCapture.cs`
- Modify: `src/ScreenRecorder.Platform.macOS/MacOsDisplayService.cs`
- Modify: `tests/ScreenRecorder.Media.Tests/MacOsDisplayServiceTests.cs`

**Interfaces:**
- Produces: `Task<SystemAudioCaptureInfo?> StartCaptureAsync(int monitorIndex, CancellationToken cancellationToken = default)`.
- Produces: `MacOsDisplayService.GetNativeDisplayId(int monitorIndex) : uint?`.
- Consumes: `RecordingConfiguration.MonitorIndex`.

- [ ] **Step 1: Write a native-display-ID mapping test**

Add to `MacOsDisplayServiceTests` using its fake `IMacOsDisplayApi`:

```csharp
[Fact]
public void GetNativeDisplayId_UsesTheSameStableEnumerationIndex()
{
    var api = new FakeDisplayApi(
        new MacOsDisplaySnapshot(91, 0, 0, 1120, 630, 2240, 1260, true),
        new MacOsDisplaySnapshot(42, 1120, 0, 1920, 1080, 1920, 1080, false));
    var service = new MacOsDisplayService(api);

    Assert.Equal((uint)42, service.GetNativeDisplayId(1));
    Assert.Equal((uint)91, service.GetNativeDisplayId(99));
}
```

The out-of-range fallback is the primary display ID.

- [ ] **Step 2: Run the mapping test and observe failure**

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~MacOsDisplayServiceTests.GetNativeDisplayId'
```

Expected: compilation fails because the method does not exist.

- [ ] **Step 3: Implement mapping and change the interface signature**

`GetNativeDisplayId` reads `_displayApi.GetActiveDisplays()`, returns the indexed snapshot ID when present, otherwise the primary ID, otherwise the first ID, otherwise null.

Change the interface and both platform implementations to:

```csharp
Task<SystemAudioCaptureInfo?> StartCaptureAsync(
    int monitorIndex,
    CancellationToken cancellationToken = default);
```

Windows explicitly discards the argument:

```csharp
_ = monitorIndex;
```

Change the engine call to:

```csharp
var sysAudioInfo = await _systemAudioLoopbackCapture.StartCaptureAsync(
    config.MonitorIndex,
    cancellationToken);
```

Rename log text from `WASAPI Loopback` to `system audio loopback` in shared engine messages; leave the Windows implementation's internal WASAPI logs unchanged.

- [ ] **Step 4: Run tests and build both platform projects**

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~MacOsDisplayServiceTests'
dotnet build src/ScreenRecorder.Platform.Windows/ScreenRecorder.Platform.Windows.csproj -c Release
dotnet build src/ScreenRecorder.Platform.macOS/ScreenRecorder.Platform.macOS.csproj -c Release
```

Expected: all commands pass.

- [ ] **Step 5: Commit**

```bash
git add src/ScreenRecorder.Core/Interfaces/ISystemAudioLoopbackCapture.cs src/ScreenRecorder.Media/Capture/FFmpegScreenRecorderEngine.cs src/ScreenRecorder.Platform.Windows/Audio/WindowsWasapiLoopbackCapture.cs src/ScreenRecorder.Platform.macOS/MacOsAudioLoopbackCapture.cs src/ScreenRecorder.Platform.macOS/MacOsDisplayService.cs tests/ScreenRecorder.Media.Tests/MacOsDisplayServiceTests.cs
git commit -m "refactor: pass display selection to system audio capture"
```

### Task 2: Add capability detection and private FIFO creation

**Files:**
- Create: `src/ScreenRecorder.Platform.macOS/MacOsSystemAudioSupport.cs`
- Create: `src/ScreenRecorder.Platform.macOS/UnixFifo.cs`
- Create: `tests/ScreenRecorder.Media.Tests/MacOsSystemAudioSupportTests.cs`

**Interfaces:**
- Produces: `MacOsSystemAudioSupport.HelperFileName` with value `OpenCam.SystemAudio`.
- Produces: `MacOsSystemAudioSupport.IsSupported(Version osVersion, string baseDirectory) : bool`.
- Produces: `UnixFifo.CreatePrivate(string path) : void`.

- [ ] **Step 1: Write capability tests**

```csharp
public class MacOsSystemAudioSupportTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "OpenCamSupport_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RequiresMacOs13AndBundledHelper()
    {
        Directory.CreateDirectory(_directory);
        Assert.False(MacOsSystemAudioSupport.IsSupported(new Version(12, 6), _directory));
        Assert.False(MacOsSystemAudioSupport.IsSupported(new Version(13, 0), _directory));

        File.WriteAllText(Path.Combine(_directory, MacOsSystemAudioSupport.HelperFileName), "fixture");
        Assert.True(MacOsSystemAudioSupport.IsSupported(new Version(13, 0), _directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
```

- [ ] **Step 2: Run the test and observe the missing type**

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~MacOsSystemAudioSupportTests'
```

Expected: compilation fails.

- [ ] **Step 3: Implement support detection and FIFO wrapper**

Capability implementation:

```csharp
public static class MacOsSystemAudioSupport
{
    public const string HelperFileName = "OpenCam.SystemAudio";

    public static string HelperPath(string baseDirectory) =>
        Path.Combine(baseDirectory, HelperFileName);

    public static bool IsSupported(Version osVersion, string baseDirectory) =>
        osVersion.Major >= 13 && File.Exists(HelperPath(baseDirectory));
}
```

`UnixFifo` P/Invokes `/usr/lib/libSystem.B.dylib`:

```csharp
[DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
private static extern int mkfifo(string path, uint mode);

internal static void CreatePrivate(string path)
{
    const uint UserReadWrite = 0x180; // 0600
    if (mkfifo(path, UserReadWrite) != 0)
        throw new IOException($"mkfifo failed ({Marshal.GetLastWin32Error()}): {path}");
}
```

- [ ] **Step 4: Run capability tests**

Run the Step 2 command.

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/ScreenRecorder.Platform.macOS/MacOsSystemAudioSupport.cs src/ScreenRecorder.Platform.macOS/UnixFifo.cs tests/ScreenRecorder.Media.Tests/MacOsSystemAudioSupportTests.cs
git commit -m "feat: detect bundled macOS system audio support"
```

### Task 3: Implement the ScreenCaptureKit audio helper

**Files:**
- Create: `src/ScreenRecorder.Platform.macOS/Native/OpenCamSystemAudio/main.swift`

**Interfaces:**
- Consumes CLI: `--fifo <absolute-path> --display-id <UInt32>`.
- Produces stderr line: `READY sample-rate=48000 channels=2 format=s16le` after stream startup.
- Produces FIFO bytes: interleaved signed 16-bit little-endian PCM at 48 kHz stereo.
- Produces nonzero exit and `ERROR <message>` on initialization/capture failure.

- [ ] **Step 1: Create argument parsing with strict validation**

Define:

```swift
struct Options {
    let fifoPath: String
    let displayID: CGDirectDisplayID

    static func parse(_ arguments: [String]) throws -> Options
}
```

`parse` accepts each required flag exactly once, requires an absolute FIFO path, parses `UInt32`, and throws `HelperError.invalidArguments`. The top-level `catch` prints `ERROR <localizedDescription>` to stderr and exits 2.

- [ ] **Step 2: Implement PCM extraction and conversion**

Add this sample-buffer conversion boundary:

```swift
extension CMSampleBuffer {
    func makePCMBuffer() throws -> AVAudioPCMBuffer {
        guard let description = CMSampleBufferGetFormatDescription(self),
              let format = AVAudioFormat(cmAudioFormatDescription: description) else {
            throw HelperError.invalidAudioFormat
        }
        let frames = AVAudioFrameCount(CMSampleBufferGetNumSamples(self))
        guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: frames) else {
            throw HelperError.invalidAudioFormat
        }
        buffer.frameLength = frames
        let status = CMSampleBufferCopyPCMDataIntoAudioBufferList(
            self,
            at: 0,
            frameCount: Int32(frames),
            into: buffer.mutableAudioBufferList)
        guard status == noErr else { throw HelperError.copyFailed(status) }
        return buffer
    }
}
```

Create a target format with:

```swift
AVAudioFormat(
    commonFormat: .pcmFormatInt16,
    sampleRate: 48_000,
    channels: 2,
    interleaved: true)!
```

`AudioWriter` owns an `AVAudioConverter` per observed source format. It converts each valid `.audio` sample into this target format and writes `mDataByteSize` bytes from the single interleaved output buffer to the FIFO `FileHandle`. Conversion errors print one `ERROR` line and terminate the run loop.

- [ ] **Step 3: Implement ScreenCaptureKit stream lifecycle**

Define `SystemAudioCapture: NSObject, SCStreamOutput, SCStreamDelegate`. In `start()`:

1. call `SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)`;
2. select the display whose `displayID` matches the CLI value, otherwise use the first display;
3. create `SCContentFilter(display:excludingApplications:exceptingWindows:)` with no exclusions;
4. create `SCStreamConfiguration` with `capturesAudio = true`, `excludesCurrentProcessAudio = true`, `sampleRate = 48_000`, `channelCount = 2`, and `showsCursor = false`;
5. add the output on a dedicated serial queue using type `.audio`;
6. call `startCapture()`;
7. print the exact `READY` line and flush stderr.

Use `signal(SIGTERM, ...)` and `signal(SIGINT, ...)` to stop capture, close the FIFO handle, and terminate the run loop. The helper never deletes the FIFO or its parent directory; the C# owner does that.

- [ ] **Step 4: Compile the helper for the minimum OS**

```bash
mkdir -p artifacts/native
xcrun swiftc src/ScreenRecorder.Platform.macOS/Native/OpenCamSystemAudio/main.swift -O -target arm64-apple-macos13.0 -framework ScreenCaptureKit -framework AVFoundation -framework CoreMedia -framework CoreGraphics -o artifacts/native/OpenCam.SystemAudio
```

Expected: compilation succeeds and `file artifacts/native/OpenCam.SystemAudio` reports a Mach-O arm64 executable.

- [ ] **Step 5: Verify argument failure is deterministic**

```bash
artifacts/native/OpenCam.SystemAudio
```

Expected: exit code 2 and a single stderr line beginning with `ERROR` that identifies the missing `--fifo` and `--display-id` arguments.

- [ ] **Step 6: Commit**

```bash
git add src/ScreenRecorder.Platform.macOS/Native/OpenCamSystemAudio/main.swift
git commit -m "feat: capture macOS system audio with ScreenCaptureKit"
```

### Task 4: Own the helper and FIFO lifecycle from C#

**Files:**
- Replace: `src/ScreenRecorder.Platform.macOS/MacOsAudioLoopbackCapture.cs`
- Create: `tests/ScreenRecorder.Media.Tests/MacOsAudioLoopbackCaptureTests.cs`

**Interfaces:**
- Consumes: `MacOsSystemAudioSupport`, `UnixFifo`, and `MacOsDisplayService.GetNativeDisplayId`.
- Produces: `SystemAudioCaptureInfo` with `-thread_queue_size 1024 -f s16le -ar 48000 -ac 2 -i <fifo>`.

- [ ] **Step 1: Write a fake-helper lifecycle test**

The Unix-only test creates an executable shell script that prints the exact READY line and waits for termination. Construct the capture with an internal testing constructor:

```csharp
internal MacOsAudioLoopbackCapture(
    string helperPath,
    Func<int, uint?> resolveDisplayId,
    Func<string> createTempDirectory,
    Action<string> createFifo)
```

Test assertions:

```csharp
var info = await capture.StartCaptureAsync(1);
Assert.NotNull(info);
Assert.Equal(48000, info.SampleRate);
Assert.Equal(2, info.Channels);
Assert.Contains("-f s16le -ar 48000 -ac 2", info.FfmpegInputArgs);
Assert.True(File.Exists(info.PipePath));
Assert.True(capture.IsCapturing);

await capture.StopCaptureAsync();
Assert.False(capture.IsCapturing);
Assert.False(Directory.Exists(Path.GetDirectoryName(info.PipePath)!));
```

- [ ] **Step 2: Run the lifecycle test and observe failure**

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~MacOsAudioLoopbackCaptureTests'
```

Expected: compilation fails because the constructor and implementation do not exist.

- [ ] **Step 3: Implement startup**

`StartCaptureAsync` must:

1. reject concurrent starts under a lock;
2. create `Path.Combine(Path.GetTempPath(), "OpenCamAudio_" + Guid.NewGuid().ToString("N"))`;
3. set Unix directory permissions to user read/write/execute;
4. create `<directory>/system-audio.pcm` using `UnixFifo.CreatePrivate`;
5. resolve the CoreGraphics display ID;
6. launch the helper with `UseShellExecute = false`, redirected stderr, and `ArgumentList` entries;
7. read stderr until the exact READY line, an ERROR line, process exit, cancellation, or a five-second timeout;
8. set `IsCapturing = true` only after READY;
9. return `SystemAudioCaptureInfo(fifo, 48000, 2, args)`.

The default constructor supplies the bundled helper path and a new `MacOsDisplayService`. `IsSupported` calls `MacOsSystemAudioSupport.IsSupported(Environment.OSVersion.Version, AppContext.BaseDirectory)`.

- [ ] **Step 4: Implement deterministic cleanup**

`StopCaptureAsync` sends SIGTERM with `Process.Kill(entireProcessTree: true)` if the helper is still running, waits at most three seconds, disposes streams/process/token sources, then deletes the private directory recursively. Call the same cleanup method after every failed start and from `DisposeAsync`. Raise `AudioErrorOccurred` for an ERROR line or unexpected helper exit.

- [ ] **Step 5: Run lifecycle and capability tests**

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~MacOsAudioLoopbackCaptureTests|FullyQualifiedName~MacOsSystemAudioSupportTests'
```

Expected: all pass and no `OpenCamAudio_*` test directory remains.

- [ ] **Step 6: Commit**

```bash
git add src/ScreenRecorder.Platform.macOS/MacOsAudioLoopbackCapture.cs tests/ScreenRecorder.Media.Tests/MacOsAudioLoopbackCaptureTests.cs
git commit -m "feat: stream macOS system audio into FFmpeg"
```

### Task 5: Separate and stabilize microphone input, then mix audio modes

**Files:**
- Modify: `tests/ScreenRecorder.Media.Tests/MacOsFFmpegProviderTests.cs`
- Modify: `src/ScreenRecorder.Platform.macOS/MacOsFFmpegProvider.cs`

**Interfaces:**
- Consumes: `systemAudioPipeArg` as a complete FFmpeg input fragment.
- Produces: deterministic input indexes: video `0`, microphone `1` when present, system audio `1` or `2`.

- [ ] **Step 1: Replace the combined-input microphone test with four mode tests**

Use `/tmp/system-audio.pcm` as the system input fixture. Assert these fragments:

```csharp
// None
Assert.Contains("-i \"Capture screen 0:none\"", noneArgs);
Assert.DoesNotContain("aresample", noneArgs);

// MicrophoneOnly
Assert.Contains("-i \"Capture screen 0:none\"", micArgs);
Assert.Contains("-thread_queue_size 1024 -f avfoundation -i \":0\"", micArgs);
Assert.Contains("[1:a]aresample=48000:async=1:first_pts=0[mic]", micArgs);
Assert.Contains("-map 0:v -map \"[mic]\"", micArgs);

// SystemOnly
Assert.Contains("-f s16le -ar 48000 -ac 2 -i \"/tmp/system-audio.pcm\"", systemArgs);
Assert.Contains("[1:a]aresample=48000:async=1:first_pts=0[sys]", systemArgs);
Assert.Contains("-map 0:v -map \"[sys]\"", systemArgs);

// SystemAndMicrophone
Assert.Contains("[1:a]aresample=48000:async=1:first_pts=0[mic]", mixedArgs);
Assert.Contains("[2:a]aresample=48000:async=1:first_pts=0[sys]", mixedArgs);
Assert.Contains("[sys][mic]amix=inputs=2:duration=longest:dropout_transition=0[aout]", mixedArgs);
Assert.Contains("-map 0:v -map \"[aout]\"", mixedArgs);
```

- [ ] **Step 2: Run provider tests and observe failure**

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~MacOsFFmpegProviderTests'
```

Expected: microphone and system-audio tests fail because the provider currently combines screen/mic and ignores `systemAudioPipeArg`.

- [ ] **Step 3: Build independent inputs and filter graphs**

Always create video input as:

```text
-thread_queue_size 1024 -f avfoundation -framerate <fps> -i "Capture screen N:none"
```

When `hasDirectShowMic` is true, append:

```text
-thread_queue_size 1024 -f avfoundation -i ":<MicrophoneDeviceId>"
```

Append nonempty `systemAudioPipeArg` after microphone input. Emit the exact filter/map fragments asserted in Step 1. Keep the existing display-relative crop as `-filter:v` before audio mapping. In recovery silence mode, preserve one mapped silent audio stream at 48 kHz.

- [ ] **Step 4: Ensure output audio is fixed at 48 kHz**

When `AudioSource != None`, output arguments must include:

```text
-c:a aac -ar 48000 -b:a <bitrate>k
```

Add an assertion for `-ar 48000` to all three audio-mode tests.

- [ ] **Step 5: Run provider tests**

Run the Step 2 command.

Expected: all provider tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/ScreenRecorder.Platform.macOS/MacOsFFmpegProvider.cs tests/ScreenRecorder.Media.Tests/MacOsFFmpegProviderTests.cs
git commit -m "fix: stabilize and mix macOS audio inputs"
```

### Task 6: Enable macOS system audio in the UI only when bundled

**Files:**
- Modify: `src/ScreenRecorder.UI/ViewModels/MainViewModel.cs`
- Modify: `src/ScreenRecorder.Core/Localization/LocalizationService.cs`
- Modify: `tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs`

**Interfaces:**
- Consumes: `MacOsSystemAudioSupport.IsSupported(Version, string)`.
- Produces: `MainViewModel.SupportsSystemAudioOnPlatform(bool isWindows, bool isMacOS, Version osVersion, string baseDirectory) : bool`.

- [ ] **Step 1: Write capability resolution tests**

Add a theory covering:

```csharp
[Theory]
[InlineData(true, false, 10, true)]
[InlineData(false, true, 12, false)]
[InlineData(false, false, 13, false)]
public void SupportsSystemAudio_RespectsPlatformAndVersion(
    bool windows, bool mac, int major, bool expected)
```

For the positive macOS 13 case, create a temporary directory containing an empty `OpenCam.SystemAudio` file and assert true. Also retain the existing `ResolveAudioSource` theory so checkbox combinations still resolve to the four enum modes.

- [ ] **Step 2: Run the test and observe the missing resolver**

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~FoolproofUiLogicTests'
```

Expected: compilation fails for the new resolver.

- [ ] **Step 3: Implement platform capability**

Add:

```csharp
internal static bool SupportsSystemAudioOnPlatform(
    bool isWindows,
    bool isMacOS,
    Version osVersion,
    string baseDirectory) =>
    isWindows ||
    (isMacOS && MacOsSystemAudioSupport.IsSupported(osVersion, baseDirectory));
```

Define `SupportsSystemAudio` using the current OS, `Environment.OSVersion.Version`, and `AppContext.BaseDirectory`. Change the macOS label from unsupported wording to the normal localized `AudioSystem` string when supported. In `LocalizationService`, change `AudioSystem` from WASAPI-specific wording to platform-neutral "錄製系統聲音" / "Record System Audio"; keep disabled explanatory text only when the helper/version check is false.

- [ ] **Step 4: Run UI logic tests and build**

```bash
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~FoolproofUiLogicTests'
dotnet build src/ScreenRecorder.UI/ScreenRecorder.UI.csproj -c Release
```

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/ScreenRecorder.UI/ViewModels/MainViewModel.cs src/ScreenRecorder.Core/Localization/LocalizationService.cs tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs
git commit -m "feat: enable bundled macOS system audio controls"
```

### Task 7: Verify native audio behavior before packaging

**Files:**
- No production changes expected.

**Interfaces:**
- Consumes: Tasks 1–6.
- Produces: compiled native helper and green automated baseline.

- [ ] **Step 1: Compile Swift with the macOS 13 deployment target**

Run the exact compile command from Task 3.

Expected: Mach-O arm64 executable.

- [ ] **Step 2: Run all tests**

```bash
dotnet test tests/ScreenRecorder.Core.Tests/ScreenRecorder.Core.Tests.csproj -c Release
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release
```

Expected: zero failures.

- [ ] **Step 3: Build the full solution**

```bash
dotnet build ScreenRecorder.sln -c Release
```

Expected: zero errors and no new warnings.

- [ ] **Step 4: Confirm Windows WASAPI implementation has only the signature adaptation**

```bash
git diff HEAD~7 -- src/ScreenRecorder.Platform.Windows/Audio/WindowsWasapiLoopbackCapture.cs src/ScreenRecorder.Platform.Windows/WindowsFFmpegProvider.cs
```

Expected: `WindowsFFmpegProvider.cs` is unchanged; the WASAPI file only accepts/discards `monitorIndex` in addition to its existing behavior.
