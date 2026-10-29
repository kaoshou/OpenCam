# Ubuntu Core Feasibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用隔離的 Linux/libobs 原生探測器，實測 X11／Wayland 與音源整合，為真正平台接入提供可重現證據，且不改變 Windows/macOS 生產行為。

**Architecture:** 先執行已核准設計的第一階段，將新原生程式、建置腳本、證據驗證與 CI 全部放在 Linux 專用目錄。先證實 pinned OBS 與 portal 在獨立 helper 中能運作，才擴充應用程式接線；這一階段不把舊診斷包改標為可用錄影器。

**Tech Stack:** Ubuntu 24.04 x64、C++17/CMake、OBS/libobs 33.0.0、X11、PipeWire/desktop portal、PulseAudio 相容音源、Node 22 evidence runner。

**Spec:** `docs/superpowers/specs/2026-10-30-ubuntu-platform-support-design.md`（使用者已核准）

## Global Constraints

- 正式版基準為 v1.2.0；應用程式基準 commit 為 `e0c369e6495532cc0393c349dc428ffb8a00327e`。
- 版本暫維持 1.2.0；只提供明確標記的 Linux 開發測試包，不合併、不改標籤、不發布正式 Release。
- Linux 使用既有 pinned OBS 33.0.0 相同來源版本：`72e86655715d9a25d69ce446aa4dd44ba58e10d2`。
- 不得為 Linux 升級現有 Avalonia、Windows/macOS OBS 或 FFmpeg 版本。
- 不讓未驗證的插件從工作目錄或任意環境路徑載入；不引入舊錄影引擎或 FFmpeg 錄影 fallback。
- Ubuntu 24.04 LTS x64、Wayland 與 X11 都是目標；Xvfb 不替代實體來源驗收。
- 實機同步標記目標：固定偏移與 5/30 分鐘漂移均不超過 100ms。
- 不要求 root、任意裝置讀取或輸入注入以繞過限制；CI 建置安裝依賴與應用程式執行權限分開。
- 本階段不改 `src/`、既有 `native/obs-capture/`、Windows/macOS 建置腳本、正式工作流程及 VERSION。

## Review Focus

1. Wayland 上 UI 經 XWayland 能開啟，但 X11 根視窗並非使用者欲錄的 Wayland 桌面；Task 3/4 必須驗證來源種類，拒絕假成功。
2. 同名音源、預設裝置改變與輸出 monitor 誤認；Task 2/3 使用實際 ID，明確記錄預檢與來源差異。
3. Portal 使用者未回應、取消或授權後關閉；Task 3/4 記錄明確狀態、有限等待與完整清理，不回報成功。
4. 載入其他 OBS 版本、意外插件或未知 ELF 依賴；Task 1/2 驗證來源、路徑與相依清單，Task 5 不公開不完整包。
5. 測試 runner 因錯誤／取消／CI 中止殘留程序與音源；Task 4 只清理由本次建立的資源，保存第一失敗原因。

## 範圍拆分與後續計畫

這是第一個可獨立驗證的子計畫，不是整個 Linux 移植完成計畫。將多個子系統一次寫死，會在 portal/headless 與依賴尚未實測前作出錯誤假設。第一階段交付原生探測器、證據與可重現建置；只有風險實測完成後，才為下列兩個階段寫精確接入計畫，不縮減已核准功能範圍：

| 已核准設計範圍 | 本計畫或後續子計畫 |
| --- | --- |
| Linux OBS build、音源/來源識別、headless portal、依賴與安全門檻 | 本計畫 Tasks 1–6 |
| `Platform.Linux`、三路工廠、ObsRuntime manifest、router、專案流程、首幀/音訊/輸出健康 | 後續 `ubuntu-recording-media`，以本階段確認的來源協定與 layout 為輸入 |
| Linux FD/spawn、BoundDirectory、預檢增益/試聽、匯入/剪輯/預覽/輸出/救援 | 同一 `ubuntu-recording-media` 子計畫，先安全元件再應用接線 |
| 教鞭、標註、快捷鍵、多螢幕/DPI、三語 UI、完整跨平台驗收 | 後續 `ubuntu-desktop-integration`，不得把 Wayland 限制默默當成功 |
| 最終使用者包、來源/授權、30 分鐘耐久與 Windows/macOS 實機 | 跨階段交付門檻；未通過不得宣稱完整支援或合併 |

## 檔案責任與介面

- `scripts/linux/obs-source-inputs.mjs`：來源與 layout 驗證，`validateLinuxObsInputs({ sourceRoot, stageRoot, evidenceRoot }) -> Promise<{ revision, obsVersion, platform }>`；版本不符拋錯，不建立 stage。
- `scripts/linux/build-obs-probe.sh`：只在 Linux x86_64 建置 pinned OBS 與探測器；參數 `OBS_SOURCE NEW_BUILD_DIRECTORY NEW_STAGE_DIRECTORY EVIDENCE_DIRECTORY`，既有 build/stage 拒絕覆寫。
- `native/obs-linux-probe/CMakeLists.txt`、`main.cpp`、`linux-sources.hpp`：Linux 專用探測器。只連結已驗證 stage，維持 libobs 錄影，JSON request stdin / MKV stdout / `OCLINUX ` JSON events stderr，不被正式 runtime 載入。
- `scripts/linux/probe-protocol.mjs`：runner 的有界協定解析與 evidence 判定，`parseProbeEvent(line) -> object | null`，`evaluateProbeEvidence({ exitCode, events, mediaProbe, fullDecodeExit }) -> { status, reasons }`。
- `scripts/linux/run-capture-probe.mjs`：`runCaptureProbe({ helper, stage, request, seconds, evidenceDirectory, signal }) -> Promise<{ status, reasons }>`，管控本次程序、檔案與清理，不傳 shell 字串執行。
- `.github/workflows/linux-core-feasibility.yml`：獨立 Linux-only CI；先不自動觸發舊 Ubuntu packaging workflow。
- `tests/scripts/linux-obs-inputs.test.mjs`、`linux-probe-protocol.test.mjs`、`linux-probe-runner.test.mjs`、`linux-isolation.test.mjs`：Node built-in test；不要求本機 Linux 即可驗證純函式與 runner 假子程序。
- `tests/native/linux-obs-probe.test.mjs`：只在明確提供 Linux helper/stage 時執行真實 libobs 整合測試；未提供環境標示 SKIP，不為通過而用 synthetic source 冒充 X11/PipeWire。
- `docs/testing/linux-core-feasibility.md`：環境、實測矩陣、限制、依賴/授權、後續接入所需決策與手動步驟。

### Task 1: 固定來源與建立隔離建置輸入

**Files:** Create `scripts/linux/obs-source-inputs.mjs`, `scripts/linux/build-obs-probe.sh`, `tests/scripts/linux-obs-inputs.test.mjs`.

**Interfaces:** Consumes spec source revision and existing `scripts/obs-dependencies.lock.json` for provenance reference only; produces validated stage and `evidence/build-inputs.json` containing source commit, dirty status, compiler, OS, dependency package versions and configured features.

- [ ] Write tests `rejectsWrongRevision`, `rejectsDirtyTrackedSources`, `rejectsExistingStage`, `rejectsStageInsideSource`, `rejectsSymlinkEscape`, `acceptsPinnedCleanInputs`; assert wrong revision/dirty/path failures never create stage.
- [ ] Run `node --test tests/scripts/linux-obs-inputs.test.mjs`; record expected failure before implementation.
- [ ] Implement `validateLinuxObsInputs(...)` with exact pinned revision, safe canonical paths, Linux/x64 guard in shell, fresh build/stage requirement and no deletion/overwrite of prior inputs.
- [ ] Configure only the OBS targets needed for libobs, OpenGL, CPU H.264/AAC, X11, PipeWire and PulseAudio compatibility. Inspect pinned CMake requirements before selecting flags. Record actual missing dependencies or version conflicts; do not substitute Ubuntu's unrelated OBS version or silently patch source. A build prerequisite conflict produces BLOCKED evidence before proceeding.
- [ ] Run Node tests and `bash -n scripts/linux/build-obs-probe.sh`; pass means all tests exit 0 and shell syntax exits 0. Record real compilation only when run on Ubuntu, not inferred from syntax.
- [ ] Commit only the three task files with `build: isolate pinned Linux OBS feasibility inputs`.

### Task 2: 原生 X11 與音源枚舉／真實擷取

**Files:** Create `native/obs-linux-probe/CMakeLists.txt`, `main.cpp`, `linux-sources.hpp`, `tests/native/linux-obs-probe.test.mjs`; extend Task 1 Linux-only build wrapper.

**Interfaces:** Consumes validated stage from Task 1. CLI `OpenCam.ObsLinuxProbe --runtime ABS_STAGE --mode enumerate|capture --backend x11|pipewire`. Capture stdin accepts existing `ObsCaptureRequest` JSON fields; `monitor` and audio IDs must be explicit values returned by enumeration. `STOP\n` requests orderly close. stdout is MKV only; stderr `OCLINUX ` events have `event`, `backend`, `stage`, `code` and no tokens/secrets. Enumerate events contain stable source IDs and actual geometry/device descriptions; no media bytes.

- [ ] Write tests `invalidRequestEmitsNoMedia`, `missingSourceDoesNotSelectFirstMonitor`, `missingAudioEndpointFailsClosed`, `enumerationDoesNotClaimRecordingReady`, `x11CapturesChangingPixels`, `selectedSilentEndpointDoesNotRequireNonzeroPeak`. Invalid geometry or nonexistent endpoint must produce nonzero exit, no ready and no media success.
- [ ] Run new native tests on a staged Ubuntu helper; capture the initial RED result. Do not run production native tests against a mismatched probe protocol.
- [ ] Implement Linux-only CMake linking installed pinned headers/libobs and explicit plugin/data directories; prohibit loading plugins from CWD. Build an actual X11 source using pinned OBS source properties, not guessed monitor index mapping. Initialize OBS video/audio/encoders and existing `native/obs-capture/mux.hpp` through read-only reuse if its interface is portable; otherwise add probe-local equivalent and record the difference, without editing shared mux code.
- [ ] Implement enumeration and exact source/device validation; use dedicated null-sink/source fixtures in CI. Readiness distinguishes source initialization, first real frame, selected audio health and output activity. No source is silently disabled, no nonzero-volume prerequisite, no silence insertion unless endpoint health is established. Preserve diagnostic failure stage and reason.
- [ ] Run real X11 changing-pattern recording plus each `none/system/microphone/both` mode, including selected silent system endpoint. Decode and inspect nonconstant frames and audio content. Record failures, do not mark unsupported cases passed.
- [ ] Commit task files after GREEN evidence with `feat: add isolated Linux libobs capture probe`.

### Task 3: Wayland Portal 與事件迴圈可行性

**Files:** Modify only `native/obs-linux-probe/main.cpp`, `linux-sources.hpp`, `CMakeLists.txt`, `tests/native/linux-obs-probe.test.mjs`; create `docs/testing/linux-core-feasibility.md`.

**Interfaces:** Same probe CLI/event framing as Task 2. Adds events `permission_wait`, `permission_cancelled`, `permission_denied`, `session_closed`, `source_ready`. `backend` must equal actual `pipewire` for Wayland acceptance. Desktop permission failure never falls back to X11. User-interaction timeout in the probe harness is 60 seconds with cancellation; it is not an application production timeout decision.

- [ ] Write tests `waylandDoesNotUseXwaylandRoot`, `portalCancelEmitsNoReady`, `portalClosedStopsCaptureAndRetainsBytes`, `permissionWaitIsNotSourceFailure`, `pipewireSourceDimensionsMatchReturnedStream`; use a real compositor/portal for integration, unit events only for parser tests.
- [ ] Inspect pinned OBS `plugins/linux-pipewire` initialization, source properties, frontend dependencies and GLib/event-loop requirements; document whether loading source in a standalone helper invokes supported portal flow. Link the exact inspected files/commit in the report.
- [ ] Run the simplest actual portal/pipewire session in an Ubuntu GNOME Wayland desktop, recording API versions and stream identity/size without logging restore tokens or remote FDs. If a compatible disposable CI desktop is available, use it as integration evidence but not physical-desktop acceptance.
- [ ] If the OBS plugin works headlessly, implement its required event loop in the Linux probe only. If frontend coupling prevents it, record BLOCKED with exact call/dependency and a concrete Linux bridge design; do not invent a desktop session or silently switch backend. A new bridge is separately planned before product code.
- [ ] Re-run new tests plus Task 2 X11/audio tests. No Wayland environment means REQUIRES MANUAL VALIDATION, not GREEN.
- [ ] Commit probe changes and measured report with `test: establish Linux portal capture feasibility`.

### Task 4: 有界證據收集與程序清理

**Files:** Create `scripts/linux/probe-protocol.mjs`, `run-capture-probe.mjs`, `tests/scripts/linux-probe-protocol.test.mjs`, `linux-probe-runner.test.mjs`.

**Interfaces:** Produces the functions listed under file responsibility. `status` is exactly `PASS`, `FAIL`, `BLOCKED`, or `REQUIRES_MANUAL_VALIDATION`. Event record is capped at 4 MiB and individual protocol line at 64 KiB; overflow is a FAIL and child cleanup is mandatory. Media output is written to a newly created evidence-directory child only, never a user recording path.

- [ ] Write tests `exitZeroWithoutReadyIsNotPass`, `decodeZeroWithoutDynamicFramesIsNotCapturePass`, `wrongBackendIsNotPass`, `audioMissingIsNotPass`, `manualEnvironmentAbsenceIsNotPass`, `malformedAndOversizedEventsFail`, `cancelReapsOwnedChild`, `firstFailureSurvivesCleanupFailure`.
- [ ] Run `node --test tests/scripts/linux-probe-protocol.test.mjs tests/scripts/linux-probe-runner.test.mjs`; assert RED before implementation.
- [ ] Implement event parser and runner via `spawn` with argument arrays, stdout file piping, bounded stderr, timeout, cancellation, STOP-first then bounded termination, and reaping; track only resources created by this run. Fixture modules use random names and remove only their known IDs. Never enumerate-and-delete unrelated user audio resources/processes.
- [ ] Write evidence files `request-redacted.json`, `events.json`, `helper.log`, `capture.mkv`, `ffprobe.json`, `decode.log`, `result.json`. Require actual media validation for PASS: expected video/audio stream presence, requested frame geometry/FPS within declared tolerance, valid timestamp progression using codec reorder semantics and changing image content. Live MKV missing format duration alone is not failure. Physical A/V sync remains manual unless an actual shared marker is measured.
- [ ] Run Node tests GREEN, plus native probe record through runner on Ubuntu; verify cancellation leaves no child or owned virtual audio module.
- [ ] Commit with `test: collect bounded Linux capture evidence safely`.

### Task 5: Ubuntu 專用 CI 與診斷套件

**Files:** Create `.github/workflows/linux-core-feasibility.yml`, `tests/scripts/linux-isolation.test.mjs`; update `docs/testing/linux-core-feasibility.md`.

**Interfaces:** workflow_dispatch on an explicit development ref only; contents read permissions, no release/upload-to-release token. Artifacts include tested commit, OS/compiler/dependency versions, source provenance, build/config logs, result.json, licenses/source coverage, SHA256SUMS. Artifact names contain `Linux-Core-Feasibility`, version and commit, never `Stable` or `Release`.

- [ ] Write tests `linuxWorkflowCannotPublishRelease`, `productionPathsUnchangedAgainstBaseline`, `linuxProbeNeverLoadsAsProductionRuntime`, `artifactContainsRevisionAndLimitations`, `missingSourceCoveragePreventsBinaryDistribution`. A build-only job may preserve logs on failure but must not publish an inadequately licensed binary.
- [ ] Run `node --test tests/scripts/linux-isolation.test.mjs` RED, then implement isolated Ubuntu workflow calling Tasks 1–4. Xvfb changing desktop + owned virtual audio exercises native X11/PulseAudio compatibility; Wayland test status is separate. Do not modify original spike's unchanged-source assertion or existing Windows/macOS jobs to accommodate new work.
- [ ] For public artifact licensing, collect OBS/other copyleft corresponding source, exact options/patches and notices using existing source-provision policy. Keep incomplete binaries local to the build; preserve logs if source coverage fails. No substitution of host FFmpeg/OBS binaries without provenance.
- [ ] Run Node tests GREEN and shell/JS syntax checks. Inspect actual staged file inventory, ELF dependencies, permissions, symlink flattening/escape checks and absence of Windows/macOS native libraries.
- [ ] Push only the development branch and manually run the Linux workflow under existing development-test authority; never master/tag/Release. If network/CI authorization is unavailable, report BLOCKED rather than fabricate an artifact.
- [ ] Download successful artifact and independently verify SHA-256 and its recorded commit. Review all jobs and skipped/manual statuses, including failure evidence. Commit only verified task changes with `ci: validate isolated Ubuntu core feasibility`.

### Task 6: 隔離驗證、後續接入決策與交付

**Files:** Update `docs/testing/linux-core-feasibility.md`, this plan checkboxes and approved design only if a new explicit decision is needed; no product code.

**Interfaces:** Produces a measured report and input decisions for subsequent `ubuntu-recording-media` plan: validated runtime layout/dependency list, source IDs/geometry, audio health signals, portal session owner/lifetime, required native protocol additions and unavailable environments.

- [ ] Run `git diff e0c369e6495532cc0393c349dc428ffb8a00327e -- src native/obs-capture scripts/build-obs-capture.sh scripts/build-obs-capture.ps1 scripts/assemble-obs-runtime.mjs .github/workflows/build-and-release.yml .github/workflows/package-release.yml VERSION`; expected empty output for protected production paths in this phase.
- [ ] Run `node --test tests/scripts/*.test.mjs tests/ci/*.test.mjs` plus Core/Media baseline suites using the available .NET 8 SDK and existing NativeOnly profile. Record true skips/platform restrictions; never turn physical requirements into unit-test claims. Source isolation is not equivalent to rerun physical Windows/macOS acceptance.
- [ ] Fill a matrix for X11/Wayland, audio modes, silence, cancellation, source loss, codec/mux checks, package/source notices and Windows/macOS unchanged paths with links to actual logs. Every missing execution is marked accordingly.
- [ ] If core feasibility passes, write the next concrete platform/recording/media plan using measured facts. If a substantial blocker remains, report exact evidence and next bounded experiment; do not silently reduce Wayland scope or ship a partial port.
- [ ] Request whole-change read-only review before committing handoff report. Commit verified evidence/docs with `docs: report measured Linux core feasibility and next gates`.
- [ ] Deliver artifact/run link only when a real artifact exists, clearly distinguish native diagnostic probe from usable OpenCam app, and list physical/desktop integration items still unverified. Do not claim all approved Linux functionality complete.

## 計畫自我檢查

- 核准設計各範圍已對應本階段或明列後續子計畫；本階段不假裝涵蓋完整移植。
- 所有新 runner/probe public entrypoints 在檔案責任或 Tasks 2/3 定義；後續 task 不改名或暗加協定。
- 五項 Review Focus 各有明確測試；X11/Wayland、虛擬/實體證據不混用。
- 安全錄影優先、既有平台不變、NativeOnly、版本/Release限制與來源授權門檻均貫穿 task。
- 使用者核准的是設計；本計畫完成後先交付審閱與選擇執行方式，尚不開始產品實作。
