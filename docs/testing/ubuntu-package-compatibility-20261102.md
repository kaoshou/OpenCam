# Ubuntu package compatibility repair (2026-11-02)

## Evidence and scope

The user's Ubuntu 25.04 / GNOME 48 installation rejects the previous Ubuntu 24.04 deb because it requires `libicu74`. Switching X11/Wayland cannot change that dependency. The official Plucky amd64 package index at `https://archive.ubuntu.com/ubuntu/dists/plucky/main/binary-amd64/Packages.gz` was inspected: it supplies `libicu76` version `76.1-1ubuntu2`. The old-releases equivalent returned HTTP 404 during this investigation; do not blindly rewrite a user's APT sources.

Microsoft documents dynamic ICU loading on Linux: https://learn.microsoft.com/en-us/dotnet/core/extensions/globalization-icu . This permits investigating a dependency alternative; it does not by itself prove this application's compatibility.

The repair declares `libicu74 | libicu76`, retains the 24.04 binary build baseline and GLIBC minimum, and teaches the archive setup helper to accept either installed alternative. Missing requirements are passed as one strictly validated dependency expression to APT's solver. No invariant-globalization workaround, ABI symlink, old library installation, or recording fallback was added.

## Local verification

- Regression RED: generated deb previously required only ICU 74; the new acceptance assertion failed.
- Grammar RED: parser API absent; then GREEN after implementation.
- Linux script suite: 76 passed, 1 skipped, 0 failed.
- Full Node packaging, CI contracts, GNOME pure tests and web-player suite: 222 passed, 3 skipped, 0 failed with normal desktop permission.
- Initial sandbox run: 221 passed, 3 skipped, 1 failed (`obs-runtime.test.mjs`, relocated AppKit helper initialization, SIGABRT). The same test and full suite passed outside the sandbox; no Mac runtime change was made.
- Shell syntax checks and `git diff --check` passed.
- Independent review found and prompted correction of a nested shell-quote error in the new test harness. No remaining Critical/Important findings. Review also prompted testing ICU removal/reinstallation and checking a mapped window rather than just a process.
- Protected Windows/macOS platform/native capture, release workflow, and VERSION diff against the established Linux baseline remains empty.

## CI results

Development CI [41343055142](https://github.com/kaoshou/OpenCam/actions/runs/41343055142) completed successfully at source `b3849e563c90c59497be6ac4f8e11e1bc637e9f8`. Both clean-install jobs passed. Ubuntu 24.04 installed ICU `74.2-1ubuntu3.1`; Ubuntu 25.04 installed ICU `76.1-1ubuntu2`. Each passed the required ELF audit, mapped-window check, uninstall/reinstall preservation and real missing-ICU prerequisite repair. The 25.04 container's official repositories worked without modification.

The application build also passed Core 384/384 and Linux managed 105/105 tests. The virtual X11 30 FPS recording produced a validated MP4 video timeline of 303.966 seconds / 9119 packets across pause/resume segments; 20 FPS comparison produced 13.2 seconds / 264 packets. MKV and MP4 timeline/dynamic-frame validation passed. These are not physical A/V perception tests.

[Download tested artifact](https://github.com/kaoshou/OpenCam/actions/runs/41343055142/artifacts/13098174927). The artifact contains `OpenCam_b3849e5_Ubuntu24.04_amd64_Beta.deb`, the extracted-application archive, corresponding sources and SHA256SUMS. All three downloaded files passed SHA256 verification; app metadata confirms version 1.2.0, exact source revision, NativeOnly true and compatibilityFallback false. Deb SHA256: `361ac70f8b6072bee4f25ce4b9109c2f01fd43d38cf933ab178070064f24c4df`.

## Acceptance boundary

The identical deb is installed on clean `ubuntu:24.04` and `ubuntu:25.04` containers. Each must resolve its own ICU, pass the archive prerequisite check, audit every required ELF dependency **before** desktop harness packages are added, display a mapped OpenCam window as a normal user, and preserve a recording sentinel through uninstall/reinstall. The extracted setup helper is also exercised with both ICU and audio-tool prerequisites removed.

This install/window test uses Xvfb and does not prove GNOME 48 Wayland capture, real audio quality, synchronization, or teaching tools. Local Docker is not running; the clean-container results above come from the authorized development CI. Existing Wayland teaching-tools work remains separate and incomplete. No release, version bump, or Windows/macOS capture changes are included.
