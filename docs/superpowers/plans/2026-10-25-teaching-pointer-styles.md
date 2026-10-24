# Teaching Pointer Styles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans inline, task by task, with one fresh final review. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Offer the four approved teaching sticks and sizes on Windows/macOS, default B, with Presentation Stick second in the cursor menu.

**Architecture:** Canonical vector artwork produces shared transparent PNG resources and metadata. Optional overlay readiness still precedes native-cursor suppression; style/size are additive recording/settings fields and never change capture or audio transport.

**Tech Stack:** Existing .NET8/Avalonia, Win32 layered overlay, AppKit helper, checked-in vector/PNG assets; no new application dependencies.

**Spec:** docs/superpowers/specs/2026-10-25-teaching-pointer-styles-design.md

**Execution status:** Tasks1–4 automated implementation/verification complete; fresh final review findings fixed. Evidence: `docs/verification/2026-10-25-teaching-pointer-styles.md` and execution ledger. Physical mixed-DPI/Retina/fullscreen/A-V acceptance remains manual. Optional new ZIP not produced; no merge/push/Release.

## Global Constraints

- Version0.4.1; no merge, push or Release. Preserve preceding uncommitted fixes and unrelated main-checkout files.
- No editable tracks, global cursor replacement, new video transport or audio/encoder changes. Preserve legacy project readers/artwork.
- B/Standard defaults; invalid settings fail safe. Cursor enum values unchanged, display order only.
- Current zh/en/ja labels only, no parentheses; historical Release notes unchanged.
- No automatic private-desktop capture or claims of physical-device PASS.

## Review Focus

- Missing/corrupt selected artwork must fall back to the ordinary captured cursor, not invisible-cursor success (Task2).
- Large artwork near negative-origin/edge coordinates must stay in bounds without a shifted hotspot (Tasks1/2).
- Old/unknown settings and unrelated settings saves must retain safe defaults/selections (Task3).
- Pause/resume, runtime/terminal failures and cancellation must not orphan duplicate overlays or restart A/V for decoration (Task2).
- Language changes and narrow/high-DPI layouts must preserve selected mode/style/size and reachability (Task3).

### Task 1: Shared artwork and safe settings contract

**Files:** Create `src/ScreenRecorder.Core/Models/TeachingPointerOptions.cs`, `src/ScreenRecorder.Core/Models/TeachingPointerArtwork.cs`, `src/ScreenRecorder.Core/Assets/TeachingPointers/` canonical SVG/PNG files; modify Core csproj, RecordingConfiguration.cs, UserSettings.cs; add `tests/ScreenRecorder.Core.Tests/TeachingPointerOptionsTests.cs` and Media `TeachingPointerArtworkTests.cs`.

**Interfaces:** `TeachingPointerStyle { WarmIvory=0, TealSilver=1, RoundShort=2, HighContrast=3 }`; `TeachingPointerSize { Small=0, Standard=1, Large=2 }`; immutable `TeachingPointerOptions(TeachingPointerStyle Style, TeachingPointerSize Size)` with static `Normalize(TeachingPointerOptions? options): TeachingPointerOptions` and B/Standard default. Add `PresentationPointer` options to recording/user settings. `TeachingPointerArtwork.Get(TeachingPointerOptions options): TeachingPointerBitmap` returns cached metadata (`ReadOnlyMemory<byte> Pixels`, int Width/Height, double HotspotX/HotspotY/VisibleExtent). Do not change the legacy PresentationPointerImage renderer.

- [ ] RED: missing-field/invalid enum normalization to B/Standard; JSON round-trip; paused config copies options without changing capture/audio; sizes have factors0.82/1/1.23. Render tests assert four distinct silhouettes/colors, transparent corners, front-tip anchoring, bounds at all sizes and source alpha.
- [ ] Run `dotnet test ScreenRecorder.sln -c Release --filter 'FullyQualifiedName~TeachingPointer'`; expected RED before implementation.
- [ ] Implement bounded immutable options/assets and cached shared rendering, keeping old pointer renderer unchanged. Generate PNGs from canonical vector source with a repeatable local build-time renderer; no runtime dependency or screenshot-derived assets.
- [ ] Re-run targeted tests; expected GREEN. Record evidence in this plan's ledger; preserve combined uncommitted work rather than partial commits.

### Task 2: Configure both live overlays without changing recorder safety

**Files:** CoreInterfaces.cs, WindowsCursorHighlightService.cs, MacOsCursorHighlightService.cs, native OpenCamCursorOverlay/main.swift, RecordingOrchestrator.Cursor.cs, verify-macos-bundle.sh; existing CursorEffectTests/MacOsCursorHighlightServiceTests/LivePresentationRecordingTests.

**Interfaces:** Extend `IPresentationPointerOverlay.StartPresentationAsync(TeachingPointerOptions options, CancellationToken cancellationToken = default): Task<bool>`. Orchestrator passes normalized session options; implementations preload selected artwork/size before ready. Update all fake implementations/callers. macOS uses validated `--stick-style`/`--stick-size` arguments for stick mode and the same resource PNGs/metadata as Windows.

- [ ] RED: actual Windows renderer uses selected style/size with premultiplied BGRA; macOS helper arguments/readiness distinguish options; missing resource/cancel/unexpected EOF clean up; orchestration preserves selected A/V, options on resume, no new sidecar, and no restart on decoration loss. Include terminal engine/audio-recovery failure cleanup regressions.
- [ ] Run targeted `CursorEffectTests|MacOsCursorHighlightServiceTests|RecordingEncoderLifecycleTests`; expected RED for the missing options path.
- [ ] Use cached assets, tip-relative positioning and size-derived padded overlay bounds. Keep 60Hz position updates, owner EOF, bounded startup and duplicate prevention. Never weaken existing safety assertions or startup deadlines.
- [ ] Run targeted tests and `xcrun swiftc .../OpenCamCursorOverlay/main.swift -o <temporary helper>` without launching a desktop overlay; expected GREEN and compile exit0.

### Task 3: Main-window selectors, menu order and persistence

**Files:** MainViewModel.cs/new focused MainViewModel teaching-pointer partial if needed, MainWindow.axaml, SettingsViewModel.cs as needed, LocalizationService.cs/JaJp.cs; CursorEffectTests/FoolproofUiLogicTests/new TeachingPointerUiTests, settings persistence tests.

**Interfaces:** Main VM exposes style/size options and selected values, `ShowPresentationPointerOptions`, and uses existing `CanEditPausedSettings` locking. Both configuration builders, paused updates, normal saves/reloads and settings-dialog saves preserve options. Four thumbnail/name choices plus Small/Standard/Large appear below cursor mode only for Presentation Stick.

- [ ] RED: all3 languages show mode order Default/PresentationStick/HighlightHalo/HaloWithClickRipple/Hidden; persisted enum selection remains correct across reordering/language changes. B/Standard defaults, four thumbnail choices, conditional visibility, recording lock, paused re-enable and JSON persistence all observable through real VMs/UI.
- [ ] Run relevant UI/localization/settings suites; expected RED before implementation.
- [ ] Implement selectors/persistence, reorder displayed modes without renumbering enum values, add all zh/en/ja names and sizes. Avoid selection-change callbacks erasing saved values during localization refresh.
- [ ] Run UI suites and inspect narrow/high-DPI headless screenshots; expected GREEN, no clipped controls or wrong selection.

### Task 4: Full verification and test-build handoff

**Files:** Current bilingual user guides, internal verification ledger, packaging notices/resources as needed.

- [ ] Update guides for four styles/sizes and baked-in/next-segment behavior; keep historical Release notes unchanged. Website guide generation must remain valid.
- [ ] Run full `dotnet test ScreenRecorder.sln -c Release`, version consistency, diff check, website tests/build, native Swift compilation and Windows x64 cross-publish. Expected exit0; report any failures and existing warnings honestly.
- [ ] Fresh read-only whole-patch review against this spec; fix important findings with observed RED/GREEN and green regression. Explicitly retain physical-device limitations.
- [ ] If producing a new test ZIP, build from immutable source snapshot, assemble current notices, verify PE/PNG contents, full archive extraction/hash and corresponding source. Do not overwrite old test ZIPs or publish Release.

## Execution note

Continue inline in the existing isolated worktree after the user's plan review. The approved visual mockup is not yet product code. The preceding DirectPointer test ZIP remains unchanged; cursor-label simplification has separately passed the complete suite.
