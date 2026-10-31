# Ubuntu deb Beta — verified packaging batch

Application/source revision: `a6be18b8370ac5ac8de66cb7382ee6e42f643bc0`, OpenCam 1.2.0. Development branch only; no merge, version/tag change or Release.

## Implemented

- Ubuntu 24.04 amd64 `.deb`, application-menu entry/icon, `/usr/bin/opencam-beta` ordinary-user launcher. APT installs declared runtime dependencies; manual `.sh` launch is no longer the recommended installed workflow.
- One dependency manifest shared with the extracted archive's prerequisite checker. No repository, audio-server or GLIBC replacement, no recording fallback.
- Build-target version suffix in Settings/About, fixed English: Windows x64 Build / macOS arm64 Build / Linux x64 Beta Build. The package entry assembly supplies metadata because the SDK removes RID from agnostic project references. Numeric project version/footer formats stay unchanged.
- Separate About OBS entry, unversioned NOTICE introduction, Linux-specific license/source inventory and locations. Debian tree generation requires project/runtime/embedded/FFmpeg notices and verifies declared native file hashes before emitting the package. This is an engineering audit, not legal certification.
- Linux new-user data paths resolved without requiring the XDG directory to exist first, used consistently by settings, export preferences and recorder logs. Windows/macOS retain their native folder lookup mode.

## Actual defects found while validating

1. Fresh-image ELF audit failed on `obs-runtime/lib/libobs-opengl.so.30`: `libwayland-egl.so.1 => not found`. The build runner had supplied it implicitly. Added `libwayland-egl1` to the common dependency declaration, with a failing-then-passing package-output regression. Installation audit now collects all ELF failures before stopping, rather than revealing only one missing library at a time.
2. After dependencies passed, fresh ordinary-user startup failed with `UnauthorizedAccessException: /ScreenRecorder`, at SettingsService constructor line 25. Linux `GetFolderPath(LocalApplicationData)` returned empty for an absent directory, turning the path relative to cwd. `DoNotVerify` resolves the actual platform data path; stores then create their owned subdirectory. The original failing clean launch and corrected successful launch are preserved in CI evidence. A separate new-XDG-directory probe verifies actual settings persistence under the requested user data location.

## Verification

Full user-delivery build and install run: https://github.com/kaoshou/OpenCam/actions/runs/41212326347 — both `linux-core` and `deb-install` passed.

Artifact with deb, extracted alternative, corresponding sources and SHA256SUMS: https://github.com/kaoshou/OpenCam/actions/runs/41212326347/artifacts/13047600668 . Logs: artifact `13047605643`.

Downloaded delivery files were verified against SHA256SUMS.txt. Deb SHA256: `40b4b38605a2f928df84ab0f4c42606bd4c2b3c1c39398c33b99a06c4487d53f`.

Independent short diagnostic: https://github.com/kaoshou/OpenCam/actions/runs/41212326780 — current managed UI with the unchanged pinned native runtime; all four jobs passed. It is not the application delivery artifact.

| Check | Observed result |
| --- | --- |
| Core regression | 384 passed, 0 failed |
| UI/media regression after path fix | 1505 passed, 26 platform/physical skips, 0 failed |
| Scripts | 143 passed, 3 platform skips, 0 failed |
| New-user settings path | Persisted under fresh XDG_DATA_HOME/ScreenRecorder, not cwd/root |
| Package identity probe | `v1.2.0 (Linux x64 Beta Build)` |
| Fresh Ubuntu24 APT install | Passed without build/development packages |
| All installed ELF dependencies | Passed; only the previously audited optional CoreCLR LTTng ABI is unavailable, no ABI symlink |
| Desktop entry | desktop-file-validate passed |
| Ordinary-user installed command | Process launch/survival passed under Xvfb; not proof of real GNOME menu interaction |
| Remove/reinstall | Recording sentinel preserved, menu file removed/reinstalled |
| 30 FPS recording | 300-second first segment + resumed segment, two original MKVs retained; preview and MP4 export passed |
| 30 FPS exported timeline | Video end 303.966 s, 9119 video packets; audio end 303.966 s (AAC priming begins at -0.021333 s) |
| 20 FPS comparison | 10-second first segment + resumed segment; MP4 video end 13.15 s, 263 video packets; preview/export passed |
| MKV/MP4 validation | Complete decode and packet-time/dynamic-frame checks passed for both frame rates |
| Windows/macOS protection | No changes to native platform/capture implementations, release workflows or VERSION against e0c369e |

Virtual X11/Pulse sources are used. Packet endpoint alignment and successful decoding do not prove perceptual synchronization, real microphone quality or correctness of every captured frame. No physical Windows/macOS recording acceptance was added by this packaging batch.

## Explicitly unfinished

Wayland product capture remains blocked; its diagnostic fixture failed and is not a support claim. Linux teaching pointer/halo/click ripple, annotation overlay and configurable global shortcuts remain unimplemented/disabled. Linux recording is currently CPU-only, Ubuntu24 x64/X11 only. Physical GNOME menu/file-manager operation, multiple monitors/fractional scaling, real devices and long-duration perceptual sync remain manual acceptance. See [readiness inventory](ubuntu-readiness-20261031.md).

## Review decisions and remaining minor

- Continue in the existing dedicated Linux checkout/branch instead of nesting another worktree; concurrent unrelated edits would require rechecking isolation (status was checked throughout).
- Nonzero `ldd` inspection is a failure, not evidence that dependencies resolved. Future legitimate static-only inputs requiring another status need an explicit reviewed exception.
- Distinguish code implementation, virtual CI acceptance and physical acceptance in status documents; CI does not replace the latter.
- Deferred minor: installed command survival and a valid desktop entry do not establish a visible window/menu activation on real GNOME. This is deliberately reported as unverified rather than silently treated as passed.

## User test

Use Ubuntu 24.04 amd64 with **Ubuntu on Xorg** selected at login. Install the downloaded deb with `sudo apt install ./OpenCam_a6be18b_Ubuntu24.04_amd64_Beta.deb`, then open **OpenCam Beta** from the application menu. Do not run the app with sudo. First record moving content plus speech/system audio for 20–30 seconds; check original MKV, editor playback and exported MP4 before longer work. Keep the original project/MKVs on any failure.
