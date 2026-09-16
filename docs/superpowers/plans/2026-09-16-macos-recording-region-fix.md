# macOS Recording and Custom Region Compatibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Make OpenCam record a selected macOS display or custom region with optional microphone audio, while preserving existing Windows behavior.

**Architecture:** Keep the UI → named-pipe recorder → platform-provider flow. Add short Unix-safe IPC names, CoreGraphics-backed display and permission services, AVFoundation screen selection by device name, and a macOS-safe region selector. Every platform difference stays behind an existing platform project or an explicit OS capability check.

**Tech Stack:** C# 12, .NET 8, Avalonia 11.2.5, xUnit, CoreGraphics P/Invoke, FFmpeg AVFoundation, Serilog.

**Spec:** docs/superpowers/specs/2026-09-16-macos-recording-region-fix-design.md

## Global Constraints

- Keep MKV as the work file and retain the current MP4 stream-copy remux.
- macOS system audio is out of scope; disable it with an explanation while keeping microphone capture available.
- Preserve current Windows display, WASAPI, FFmpeg, IPC, and region-selector behavior.
- A macOS custom region belongs to one display and is clamped to that display.
- Add no NuGet dependencies.
- Use red-green TDD for each production change.
- Live Windows capture remains REQUIRES MANUAL VALIDATION on this macOS host.

## File Map

- src/ScreenRecorder.Infrastructure/IPC/SessionPipeNameFactory.cs: platform-safe unique IPC names.
- src/ScreenRecorder.Infrastructure/IPC/NamedPipeIpcServer.cs: listener failure logging.
- src/ScreenRecorder.Platform.macOS/CoreGraphicsDisplayApi.cs: CoreGraphics display P/Invoke.
- src/ScreenRecorder.Platform.macOS/MacOsDisplayService.cs: native snapshots to MonitorInfo.
- src/ScreenRecorder.Platform.macOS/MacOsScreenCapturePermissionService.cs: permission preflight/request.
- src/ScreenRecorder.Platform.macOS/MacOsFFmpegProvider.cs: AVFoundation screen and crop arguments.
- src/ScreenRecorder.UI/Views/RegionSelectionGeometry.cs: physical-pixel region calculations.
- src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml.cs: OS-specific native/fallback bounds.
- src/ScreenRecorder.UI/ViewModels/MainViewModel.cs: permission and audio capability enforcement.
- src/ScreenRecorder.UI/Views/MainWindow.axaml: capability presentation.
- tests/ScreenRecorder.Core.Tests/IpcTests.cs: safe naming and real communication.
- tests/ScreenRecorder.Media.Tests/MacOsDisplayServiceTests.cs: display conversion.
- tests/ScreenRecorder.Media.Tests/MacOsFFmpegProviderTests.cs: screen input/crop/microphone.
- tests/ScreenRecorder.Media.Tests/MacOsPermissionTests.cs: permission delegation.
- tests/ScreenRecorder.Media.Tests/RegionSelectionGeometryTests.cs: Retina geometry and clamping.
- tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs: audio-source normalization.
- MANUAL_TEST_CHECKLIST.md and ACCEPTANCE_REPORT.md: executed evidence.

---

### Task 1: Make recorder IPC names safe on macOS

**Files:**
- Create: src/ScreenRecorder.Infrastructure/IPC/SessionPipeNameFactory.cs
- Modify: src/ScreenRecorder.Infrastructure/IPC/NamedPipeIpcServer.cs:30-90
- Modify: src/ScreenRecorder.UI/ViewModels/MainViewModel.cs:1125-1146
- Modify: tests/ScreenRecorder.Core.Tests/IpcTests.cs

**Interfaces:**
- Produces: SessionPipeNameFactory.Create(bool isWindows) : string
- Consumes: NamedPipeConstants.PipeBaseName

- [ ] **Step 1: Write failing name and communication tests**

Add to IpcTests.cs:

    [Fact]
    public void SessionPipeName_OnUnix_FitsMacOsUnixSocketPath()
    {
        var name = SessionPipeNameFactory.Create(isWindows: false);
        var path = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + name);
        Assert.StartsWith("oc_", name);
        Assert.True(Encoding.UTF8.GetByteCount(path) <= 103, path);
    }

    [Fact]
    public void SessionPipeName_OnWindows_PreservesExistingPrefix()
    {
        var name = SessionPipeNameFactory.Create(isWindows: true);
        Assert.StartsWith(NamedPipeConstants.PipeBaseName + "_", name);
    }

    [Fact]
    public async Task UnixSafeSessionPipe_Communicates()
    {
        var name = SessionPipeNameFactory.Create(isWindows: false);
        await using var server = new NamedPipeIpcServer(
            name,
            _ => Task.FromResult(new IpcResponse { Success = true }));
        server.Start();
        await Task.Delay(100);
        await using var client = new NamedPipeIpcClient(name);
        var response = await client.SendCommandAsync("Ping", new { }, 2000);
        Assert.True(response.Success, response.ErrorMessage);
    }

Add using System.Text.

- [ ] **Step 2: Run RED**

    DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Core.Tests/ScreenRecorder.Core.Tests.csproj -c Debug --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~IpcTests

Expected: compilation fails because SessionPipeNameFactory is absent.

- [ ] **Step 3: Implement the minimal factory**

Create SessionPipeNameFactory.cs:

    namespace ScreenRecorder.Infrastructure.IPC;

    public static class SessionPipeNameFactory
    {
        public static string Create(bool isWindows)
        {
            var token = Guid.NewGuid().ToString("N");
            return isWindows
                ? NamedPipeConstants.PipeBaseName + "_" + token
                : "oc_" + token[..16];
        }
    }

Replace MainViewModel's GUID construction with:

    _currentPipeName = SessionPipeNameFactory.Create(OperatingSystem.IsWindows());

- [ ] **Step 4: Log listener exceptions**

Add using Serilog and replace the bare catch in NamedPipeIpcServer:

    catch (Exception ex)
    {
        if (cancellationToken.IsCancellationRequested) break;
        Log.Error(ex, "IPC listener failed for pipe {PipeName}", _pipeName);
        try { await Task.Delay(50, cancellationToken); }
        catch (OperationCanceledException) { break; }
    }

- [ ] **Step 5: Run GREEN**

Run the Step 2 command. Expected: all IpcTests pass.

- [ ] **Step 6: Commit**

    git add src/ScreenRecorder.Infrastructure/IPC/SessionPipeNameFactory.cs src/ScreenRecorder.Infrastructure/IPC/NamedPipeIpcServer.cs src/ScreenRecorder.UI/ViewModels/MainViewModel.cs tests/ScreenRecorder.Core.Tests/IpcTests.cs
    git commit -m "fix: use macOS-safe recorder pipe names"

---

### Task 2: Enumerate actual macOS displays

**Files:**
- Create: src/ScreenRecorder.Platform.macOS/CoreGraphicsDisplayApi.cs
- Modify: src/ScreenRecorder.Platform.macOS/MacOsDisplayService.cs
- Modify: src/ScreenRecorder.Platform.macOS/ScreenRecorder.Platform.macOS.csproj
- Modify: tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj
- Create: tests/ScreenRecorder.Media.Tests/MacOsDisplayServiceTests.cs

**Interfaces:**
- Produces: IMacOsDisplayApi.GetActiveDisplays() : IReadOnlyList<MacOsDisplaySnapshot>
- Produces: MacOsDisplaySnapshot(uint Id, double X, double Y, double Width, double Height, nuint PixelWidth, nuint PixelHeight, bool IsPrimary)
- Produces: zero-based MonitorInfo entries.

- [ ] **Step 1: Expose internals and reference the macOS project**

Add ScreenRecorder.Platform.macOS as a project reference in ScreenRecorder.Media.Tests.csproj. Add this to the macOS project:

    <ItemGroup>
      <InternalsVisibleTo Include="ScreenRecorder.Media.Tests" />
    </ItemGroup>

- [ ] **Step 2: Write failing service tests**

Create MacOsDisplayServiceTests.cs:

    public class MacOsDisplayServiceTests
    {
        [Fact]
        public void GetMonitors_UsesNativePixelsAndZeroBasedIndices()
        {
            var api = new FakeDisplayApi(
                new MacOsDisplaySnapshot(
                    42, 0, 0, 2240, 1260, 4480, 2520, true));
            var service = new MacOsDisplayService(api);

            var monitor = Assert.Single(service.GetMonitors());

            Assert.Equal(0, monitor.Index);
            Assert.Equal(4480, monitor.Bounds.Width);
            Assert.Equal(2520, monitor.Bounds.Height);
            Assert.Equal(2.0, monitor.DpiScaling, 3);
            Assert.True(monitor.IsPrimary);
        }

        [Fact]
        public void GetMonitors_WithNoNativeDisplays_UsesSafeFallback()
        {
            var service = new MacOsDisplayService(new FakeDisplayApi());
            var monitor = Assert.Single(service.GetMonitors());
            Assert.Equal(0, monitor.Index);
            Assert.Equal(1920, monitor.Bounds.Width);
            Assert.Equal(1080, monitor.Bounds.Height);
        }

        private sealed class FakeDisplayApi(
            params MacOsDisplaySnapshot[] displays) : IMacOsDisplayApi
        {
            public IReadOnlyList<MacOsDisplaySnapshot>
                GetActiveDisplays() => displays;
        }
    }

- [ ] **Step 3: Run RED**

    DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Debug --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~MacOsDisplayServiceTests

Expected: compilation fails because the API abstractions and injectable constructor are absent.

- [ ] **Step 4: Implement CoreGraphics access**

Create CoreGraphicsDisplayApi.cs with:

    internal record struct MacOsDisplaySnapshot(
        uint Id, double X, double Y, double Width, double Height,
        nuint PixelWidth, nuint PixelHeight, bool IsPrimary);

    internal interface IMacOsDisplayApi
    {
        IReadOnlyList<MacOsDisplaySnapshot> GetActiveDisplays();
    }

Add CGRect-compatible sequential structs and these imports:

    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(CoreGraphics)]
    private static extern int CGGetActiveDisplayList(
        uint maxDisplays,
        [Out] uint[]? displays,
        out uint displayCount);
    [DllImport(CoreGraphics)]
    private static extern CGRect CGDisplayBounds(uint display);
    [DllImport(CoreGraphics)]
    private static extern nuint CGDisplayPixelsWide(uint display);
    [DllImport(CoreGraphics)]
    private static extern nuint CGDisplayPixelsHigh(uint display);
    [DllImport(CoreGraphics)]
    private static extern int CGDisplayIsMain(uint display);

Implement GetActiveDisplays with a count call followed by a populated-array call. Map each returned ID to MacOsDisplaySnapshot. Return Array.Empty when either native call fails or the count is zero.

- [ ] **Step 5: Replace placeholder display conversion**

Add a public default constructor and internal injected constructor to MacOsDisplayService. Convert snapshots with:

    var scale = display.Width > 0
        ? (double)display.PixelWidth / display.Width
        : 1.0;
    var bounds = new CaptureRegion(
        (int)Math.Round(display.X * scale),
        (int)Math.Round(display.Y * scale),
        (int)display.PixelWidth,
        (int)display.PixelHeight);
    return new MonitorInfo(
        index,
        "Mac Display " + (index + 1),
        bounds,
        display.IsPrimary,
        scale);

Derive GetPrimaryMonitor and GetVirtualScreenBounds from GetMonitors. Use constants only in the zero-display fallback.

- [ ] **Step 6: Run GREEN**

Run Step 3. Expected: both display tests pass.

- [ ] **Step 7: Commit**

    git add src/ScreenRecorder.Platform.macOS/CoreGraphicsDisplayApi.cs src/ScreenRecorder.Platform.macOS/MacOsDisplayService.cs src/ScreenRecorder.Platform.macOS/ScreenRecorder.Platform.macOS.csproj tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj tests/ScreenRecorder.Media.Tests/MacOsDisplayServiceTests.cs
    git commit -m "feat: enumerate native macOS displays"

---

### Task 3: Select AVFoundation screens by name and crop relative to the display

**Files:**
- Modify: src/ScreenRecorder.Platform.macOS/MacOsFFmpegProvider.cs
- Create: tests/ScreenRecorder.Media.Tests/MacOsFFmpegProviderTests.cs

**Interfaces:**
- Consumes: IDisplayService from Task 2.
- Produces: MacOsFFmpegProvider(IDisplayService displayService).
- Produces: -i "Capture screen N:audio" AVFoundation input.

- [ ] **Step 1: Write failing argument tests**

Create MacOsFFmpegProviderTests.cs with a fake display service and these tests:

    [Fact]
    public void FullDisplay_SelectsNamedScreenWithoutCrop()
    {
        var provider = CreateProvider();
        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.Monitor,
            MonitorIndex = 1,
            AudioSource = AudioSourceType.None,
            Fps = 30
        };
        var args = provider.BuildInputArguments(
            config, 4480, 0, 1920, 1080, false, false);
        Assert.Contains("-i \"Capture screen 1:none\"", args);
        Assert.DoesNotContain("crop=", args);
    }

    [Fact]
    public void CustomRegion_UsesDisplayRelativeCrop()
    {
        var provider = CreateProvider();
        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            MonitorIndex = 1,
            AudioSource = AudioSourceType.None
        };
        var args = provider.BuildInputArguments(
            config, 4580, 50, 640, 480, false, false);
        Assert.Contains("crop=640:480:100:50", args);
    }

    [Fact]
    public void Microphone_UsesConfiguredAudioDevice()
    {
        var provider = CreateProvider();
        var config = new RecordingConfiguration
        {
            MonitorIndex = 0,
            AudioSource = AudioSourceType.MicrophoneOnly,
            MicrophoneDeviceId = "2"
        };
        var args = provider.BuildInputArguments(
            config, 0, 0, 1280, 720, false, true);
        Assert.Contains("-i \"Capture screen 0:2\"", args);
    }

    private static MacOsFFmpegProvider CreateProvider() =>
        new(new FakeDisplayService(
            new MonitorInfo(
                0, "Mac Display 1",
                new CaptureRegion(0, 0, 4480, 2520), true, 2.0),
            new MonitorInfo(
                1, "Mac Display 2",
                new CaptureRegion(4480, 0, 1920, 1080), false, 1.0)));

    private sealed class FakeDisplayService(
        params MonitorInfo[] monitors) : IDisplayService
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => monitors;
        public MonitorInfo? GetPrimaryMonitor() =>
            monitors.FirstOrDefault(m => m.IsPrimary);
        public CaptureRegion GetVirtualScreenBounds() =>
            monitors[0].Bounds;
    }

- [ ] **Step 2: Run RED**

    DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Debug --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~MacOsFFmpegProviderTests

Expected: constructor failure and numeric device mismatch.

- [ ] **Step 3: Implement injected display resolution**

Add:

    private readonly IDisplayService _displayService;

    public MacOsFFmpegProvider() : this(new MacOsDisplayService()) { }

    public MacOsFFmpegProvider(IDisplayService displayService)
    {
        _displayService = displayService;
    }

Resolve the display by config.MonitorIndex, falling back to the primary display. Build the video name from display.Index. Use config.MicrophoneDeviceId only when AudioSource requests a microphone; otherwise use none.

For custom regions calculate:

    var localX = Math.Max(0, x - (display?.Bounds.X ?? 0));
    var localY = Math.Max(0, y - (display?.Bounds.Y ?? 0));
    args.Add("-filter:v \"crop=" + width + ":" + height + ":" + localX + ":" + localY + "\"");

Do not modify WindowsFFmpegProvider.

- [ ] **Step 4: Confirm recorder DI uses the IDisplayService constructor**

Microsoft DI should choose MacOsFFmpegProvider(IDisplayService). Keep the parameterless constructor for UI encoder probing.

- [ ] **Step 5: Run GREEN**

Run Step 2. Expected: all provider tests pass.

- [ ] **Step 6: Commit**

    git add src/ScreenRecorder.Platform.macOS/MacOsFFmpegProvider.cs tests/ScreenRecorder.Media.Tests/MacOsFFmpegProviderTests.cs
    git commit -m "fix: select macOS screen capture devices by name"

---

### Task 4: Add screen permission and audio capability handling

**Files:**
- Create: src/ScreenRecorder.Platform.macOS/MacOsScreenCapturePermissionService.cs
- Modify: src/ScreenRecorder.UI/ViewModels/MainViewModel.cs
- Modify: src/ScreenRecorder.UI/Views/MainWindow.axaml
- Modify: src/ScreenRecorder.UI/Localization/LanguageManager.cs
- Create: tests/ScreenRecorder.Media.Tests/MacOsPermissionTests.cs
- Modify: tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs

**Interfaces:**
- Produces: IMacOsScreenCapturePermissionService.HasPermission() and RequestPermission().
- Produces: MainViewModel.ResolveAudioSource(bool supportsSystemAudio, bool recordSystemAudio, bool recordMicrophone).

- [ ] **Step 1: Write failing permission tests**

Create MacOsPermissionTests.cs:

    [Fact]
    public void HasPermission_DelegatesToPreflight()
    {
        var api = new FakePermissionApi { PreflightResult = true };
        var service = new MacOsScreenCapturePermissionService(api);
        Assert.True(service.HasPermission());
        Assert.Equal(1, api.PreflightCalls);
    }

    [Fact]
    public void RequestPermission_DelegatesToRequest()
    {
        var api = new FakePermissionApi { RequestResult = true };
        var service = new MacOsScreenCapturePermissionService(api);
        Assert.True(service.RequestPermission());
        Assert.Equal(1, api.RequestCalls);
    }

    private sealed class FakePermissionApi
        : IMacOsScreenCapturePermissionApi
    {
        public bool PreflightResult { get; init; }
        public bool RequestResult { get; init; }
        public int PreflightCalls { get; private set; }
        public int RequestCalls { get; private set; }
        public bool Preflight()
        {
            PreflightCalls++;
            return PreflightResult;
        }
        public bool Request()
        {
            RequestCalls++;
            return RequestResult;
        }
    }

- [ ] **Step 2: Write failing audio resolver tests**

Add to FoolproofUiLogicTests:

    [Theory]
    [InlineData(false, true, false, AudioSourceType.None)]
    [InlineData(false, true, true, AudioSourceType.MicrophoneOnly)]
    [InlineData(true, true, false, AudioSourceType.SystemOnly)]
    [InlineData(true, true, true, AudioSourceType.SystemAndMicrophone)]
    public void ResolveAudioSource_RespectsCapabilities(
        bool supportsSystemAudio,
        bool recordSystemAudio,
        bool recordMicrophone,
        AudioSourceType expected)
    {
        Assert.Equal(
            expected,
            MainViewModel.ResolveAudioSource(
                supportsSystemAudio,
                recordSystemAudio,
                recordMicrophone));
    }

- [ ] **Step 3: Run RED**

    DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Debug --no-restore -m:1 -p:UseSharedCompilation=false --filter "FullyQualifiedName~MacOsPermissionTests|FullyQualifiedName~FoolproofUiLogicTests"

Expected: permission service and resolver compilation failures.

- [ ] **Step 4: Implement the permission service**

Create an internal native API calling CoreGraphics:

    internal interface IMacOsScreenCapturePermissionApi
    {
        bool Preflight();
        bool Request();
    }

    internal sealed class CoreGraphicsScreenCapturePermissionApi
        : IMacOsScreenCapturePermissionApi
    {
        private const string CoreGraphics =
            "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

        [DllImport(CoreGraphics)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CGPreflightScreenCaptureAccess();

        [DllImport(CoreGraphics)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CGRequestScreenCaptureAccess();

        public bool Preflight() => CGPreflightScreenCaptureAccess();
        public bool Request() => CGRequestScreenCaptureAccess();
    }

Add the public service:

    public interface IMacOsScreenCapturePermissionService
    {
        bool HasPermission();
        bool RequestPermission();
    }

    public sealed class MacOsScreenCapturePermissionService
        : IMacOsScreenCapturePermissionService
    {
        private readonly IMacOsScreenCapturePermissionApi _api;
        public MacOsScreenCapturePermissionService()
            : this(new CoreGraphicsScreenCapturePermissionApi()) { }
        internal MacOsScreenCapturePermissionService(
            IMacOsScreenCapturePermissionApi api) => _api = api;
        public bool HasPermission() => _api.Preflight();
        public bool RequestPermission() => _api.Request();
    }

- [ ] **Step 5: Implement UI platform capability logic**

Add:
- SupportsSystemAudio returning OperatingSystem.IsWindows().
- CanRecordSystemAudio requiring support and an idle UI.
- SystemAudioLabel selecting AudioSystem or AudioSystemUnsupportedMac.
- internal static ResolveAudioSource with the four-case switch.
- A macOS permission-service field initialized only on macOS.

During settings load use:

    RecordSystemAudio = SupportsSystemAudio && s.RecordSystemAudio;

Before EnsureRecorderProcessAsync, preflight/request permission. If request returns false, set StatusScreenPermissionRequired and return without starting the recorder. Build RecordingConfiguration.AudioSource with ResolveAudioSource.

- [ ] **Step 6: Update XAML and localization**

Bind the system-audio checkbox to CanRecordSystemAudio and SystemAudioLabel. Add:

    zh-TW AudioSystemUnsupportedMac:
    macOS 系統聲音（此版本尚未支援）

    zh-TW StatusScreenPermissionRequired:
    需要 macOS 螢幕錄製權限；請在「系統設定 → 隱私權與安全性 → 螢幕與系統音訊錄製」允許 OpenCam，然後重新啟動。

Add equivalent English strings.

- [ ] **Step 7: Run GREEN**

Run Step 3. Expected: selected tests pass.

- [ ] **Step 8: Commit**

    git add src/ScreenRecorder.Platform.macOS/MacOsScreenCapturePermissionService.cs src/ScreenRecorder.UI/ViewModels/MainViewModel.cs src/ScreenRecorder.UI/Views/MainWindow.axaml src/ScreenRecorder.UI/Localization/LanguageManager.cs tests/ScreenRecorder.Media.Tests/MacOsPermissionTests.cs tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs
    git commit -m "feat: handle macOS screen recording permission"

---

### Task 5: Make custom-region geometry and presentation work on macOS

**Files:**
- Create: src/ScreenRecorder.UI/Views/RegionSelectionGeometry.cs
- Modify: src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml
- Modify: src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml.cs
- Modify: src/ScreenRecorder.UI/ViewModels/MainViewModel.cs
- Create: tests/ScreenRecorder.Media.Tests/RegionSelectionGeometryTests.cs

**Interfaces:**
- Produces: RegionSelectionGeometry.FromWindow(PixelPoint, Size, double) : CaptureRegion.
- Produces: RegionSelectionGeometry.ClampToBounds(CaptureRegion, CaptureRegion) : CaptureRegion.
- Consumes: Task 2 monitor bounds.

- [ ] **Step 1: Write failing geometry tests**

Create RegionSelectionGeometryTests.cs:

    [Fact]
    public void FromWindow_ConvertsRetinaSizeToEvenPhysicalPixels()
    {
        var result = RegionSelectionGeometry.FromWindow(
            new PixelPoint(100, 80),
            new Size(641.5, 480.5),
            2.0);
        Assert.Equal(
            new CaptureRegion(100, 80, 1282, 960),
            result);
    }

    [Fact]
    public void ClampToBounds_ClampsToOneDisplay()
    {
        var result = RegionSelectionGeometry.ClampToBounds(
            new CaptureRegion(6300, 1000, 400, 300),
            new CaptureRegion(4480, 0, 1920, 1080));
        Assert.Equal(
            new CaptureRegion(6000, 780, 400, 300),
            result);
    }

- [ ] **Step 2: Run RED**

    DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Debug --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~RegionSelectionGeometryTests

Expected: RegionSelectionGeometry is absent.

- [ ] **Step 3: Implement pure geometry**

Create RegionSelectionGeometry:

    internal static class RegionSelectionGeometry
    {
        public static CaptureRegion FromWindow(
            PixelPoint position,
            Size clientSize,
            double renderScaling)
        {
            var scale = renderScaling > 0 ? renderScaling : 1.0;
            return new CaptureRegion(
                position.X,
                position.Y,
                ToEven((int)Math.Round(clientSize.Width * scale)),
                ToEven((int)Math.Round(clientSize.Height * scale)));
        }

        public static CaptureRegion ClampToBounds(
            CaptureRegion region,
            CaptureRegion bounds)
        {
            var width = ToEven(Math.Min(region.Width, bounds.Width));
            var height = ToEven(Math.Min(region.Height, bounds.Height));
            var x = Math.Clamp(
                region.X,
                bounds.X,
                bounds.X + bounds.Width - width);
            var y = Math.Clamp(
                region.Y,
                bounds.Y,
                bounds.Y + bounds.Height - height);
            return new CaptureRegion(x, y, width, height);
        }

        private static int ToEven(int value) =>
            value % 2 == 0 ? value : value - 1;
    }

- [ ] **Step 4: Guard the Windows native call**

In CaptureFinalBounds call GetWindowRect only when OperatingSystem.IsWindows(). On macOS and fallback paths, use:

    var region = RegionSelectionGeometry.FromWindow(
        Position,
        ClientSize,
        RenderScaling);
    _selectedX = region.X;
    _selectedY = region.Y;
    _selectedWidth = region.Width;
    _selectedHeight = region.Height;

- [ ] **Step 5: Add the visible macOS transparency fallback**

In the macOS constructor path set:

    TransparencyLevelHint = new[]
    {
        WindowTransparencyLevel.Blur,
        WindowTransparencyLevel.Transparent,
        WindowTransparencyLevel.None
    };
    TransparencyBackgroundFallback =
        new SolidColorBrush(Color.Parse("#660F172A"));

Keep SystemDecorations=None, Topmost=True, the green border, and current move/resize/preset handlers. Do not change the Windows transparency path.

- [ ] **Step 6: Associate and clamp the selected macOS region**

In MainViewModel.UpdateCustomRegion, only on macOS:
1. Find the monitor containing the region center.
2. Select the matching MonitorDisplayOption by monitor index.
3. Clamp with RegionSelectionGeometry.ClampToBounds before persistence.

Leave the current Windows update path unchanged.

- [ ] **Step 7: Run GREEN**

Run Step 2. Expected: geometry tests pass.

- [ ] **Step 8: Commit**

    git add src/ScreenRecorder.UI/Views/RegionSelectionGeometry.cs src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml src/ScreenRecorder.UI/Views/RegionSelectWindow.axaml.cs src/ScreenRecorder.UI/ViewModels/MainViewModel.cs tests/ScreenRecorder.Media.Tests/RegionSelectionGeometryTests.cs
    git commit -m "fix: show and normalize macOS custom regions"

---

### Task 6: Verify the complete path and record evidence

**Files:**
- Modify: MANUAL_TEST_CHECKLIST.md
- Modify: ACCEPTANCE_REPORT.md

**Interfaces:**
- Consumes: Tasks 1-5.
- Produces: reproducible build, test, GUI, recording, and ffprobe evidence.

- [ ] **Step 1: Run all tests**

    DOTNET_ROLL_FORWARD=Major dotnet test ScreenRecorder.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false

Expected: zero failed runnable tests. Record exact skipped hardware tests.

- [ ] **Step 2: Run Release build**

    dotnet build ScreenRecorder.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false

Expected: exit 0. Record warnings verbatim.

- [ ] **Step 3: Publish a self-contained macOS build**

    dotnet publish src/ScreenRecorder.UI/ScreenRecorder.UI.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/macos/publish

Copy local ffmpeg and ffprobe into the publish directory. Build artifacts/macos/OpenCam.app using the repository workflow's Info.plist descriptions and ad-hoc sign it. Do not modify /Applications/OpenCam.app.

Use:

    mkdir -p artifacts/macos/OpenCam.app/Contents/MacOS
    mkdir -p artifacts/macos/OpenCam.app/Contents/Resources
    cp -R artifacts/macos/publish/. artifacts/macos/OpenCam.app/Contents/MacOS/
    cp /opt/homebrew/bin/ffmpeg artifacts/macos/OpenCam.app/Contents/MacOS/ffmpeg
    cp /opt/homebrew/bin/ffprobe artifacts/macos/OpenCam.app/Contents/MacOS/ffprobe
    cp src/ScreenRecorder.UI/Assets/app_icon.png artifacts/macos/OpenCam.app/Contents/Resources/app_icon.png
    chmod +x artifacts/macos/OpenCam.app/Contents/MacOS/OpenCam
    chmod +x artifacts/macos/OpenCam.app/Contents/MacOS/ffmpeg
    chmod +x artifacts/macos/OpenCam.app/Contents/MacOS/ffprobe

Create artifacts/macos/OpenCam.app/Contents/Info.plist with apply_patch, copying the complete Info.plist dictionary from .github/workflows/build-and-release.yml lines 108-139. Then run:

    codesign --force --deep -s - artifacts/macos/OpenCam.app

- [ ] **Step 4: Verify GUI behavior**

Launch through LaunchServices and verify:
1. actual display resolution
2. disabled macOS system-audio control
3. visible movable/resizable green region border
4. Escape cancel and Enter confirm.

Before changing any macOS privacy/system setting, stop and request action-time user confirmation.

- [ ] **Step 5: Record and stop**

With permission available, record visible motion in a custom region for at least five seconds, stop normally, and confirm MKV plus MP4 output.

- [ ] **Step 6: Validate with ffprobe**

Resolve the newest default-output MP4 and probe it:

    recording_candidates=(/Users/yhcheng/Movies/ScreenRecordings/*.mp4(N.om))
    test ${#recording_candidates[@]} -gt 0
    ffprobe -v error -show_entries format=duration,size -show_entries stream=codec_type,codec_name,width,height,r_frame_rate,sample_rate -of json "${recording_candidates[1]}"

Expected: nonzero duration/size, H.264 video, selected dimensions, and no audio stream when microphone is disabled.

- [ ] **Step 7: Update acceptance documents**

Record OS, architecture, SDK, FFmpeg, commit, every command/result, log/MKV/MP4 paths, ffprobe JSON, and known limitations. Mark Windows live capture REQUIRES MANUAL VALIDATION. Do not mark microphone passed unless an audio stream was actually verified.

- [ ] **Step 8: Run final verification**

    git diff --check
    dotnet build ScreenRecorder.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
    DOTNET_ROLL_FORWARD=Major dotnet test ScreenRecorder.sln -c Release --no-build -m:1

Expected: diff check silent, build exit 0, all runnable tests pass.

- [ ] **Step 9: Commit evidence**

    git add MANUAL_TEST_CHECKLIST.md ACCEPTANCE_REPORT.md
    git commit -m "docs: record macOS compatibility verification"
