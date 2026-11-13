# Homepage downloads

The homepages provide three direct attachment links: Windows x64 portable ZIP,
Windows x64 installer EXE and macOS Apple Silicon arm64 DMG. When verified Linux
Beta assets are available, a fourth “Download Ubuntu (Beta)” button downloads the
deb installer. The GitHub Releases listing, localized guide and Ubuntu Beta archive
remain text links below the buttons, alongside platform requirements.

`downloads.json` pins the **published** download version independently of the
development `VERSION`. The homepage's current-version label uses this published
version too. Do not advance it merely because application VERSION changed.

When updating downloads:

1. Confirm the target GitHub Release is public and not a draft/prerelease.
2. Verify all three exact names exist in uploaded state and are non-empty:
   `OpenCam_v<version>_Windows_x64_Portable.zip`,
   `OpenCam_v<version>_Windows_x64_Setup.exe`,
   `OpenCam_v<version>_macOS_arm64.dmg`.
3. Update `downloads.json` and the explicit published-version assertion in
   `tests/home.test.mjs`. Preserve the published-versus-development version guards;
   run the website suite/build and browser layout checks.
4. Deploy the website only after confirming the public attachment URLs.

There is no browser-side API call or automatic OS detection. All three platforms
remain visible, including when visiting from a phone or a different computer.
GitHub serves the files, so browser download preferences still apply.

## Linux Beta

The notes directly below the top download buttons identify Ubuntu 24.04 LTS x64
as the primary test target, explain X11 versus Wayland capability limits, system
screen-sharing consent and CPU encoding, and retain the need for GNOME/hardware
real-device validation and a short recording/replay before important work. There
is no separate Ubuntu promotional section. These notes do not imply full desktop
parity or that a successful cross-platform build validates every Linux desktop.

Keep Ubuntu 25.04's ICU 76 dependency work as a historical compatibility record,
not as a recommended operating system. Ubuntu 25.04 reached end of life on
2026-01-15 and no longer receives regular package or security updates; it is not
recommended for new installations or production use. Other Ubuntu versions need
separate compatibility validation. Do not list Ubuntu 26.04 LTS as validated for
OpenCam merely because the operating system is officially supported. Preserve
completed test records without presenting them as current OS security support.
Apply the same distinction in Traditional Chinese and English copy.
Sources: [Ubuntu 25.04 end-of-life announcement](https://lists.ubuntu.com/archives/ubuntu-announce/2026-January/000320.html)
and [Ubuntu release lifecycle](https://ubuntu.com/about).

Set `linuxBetaVersion` in `downloads.json` to the published `version` only after
verifying `OpenCam_v<version>_Linux_x64_Beta.deb`,
`OpenCam_v<version>_Linux_x64_Beta.tar.gz`,
`OpenCam_v<version>_CorrespondingSource.tar.gz` and
`OpenCam_v<version>_SHA256SUMS.txt` exist with verified hashes in that
public Release. An absent flag intentionally hides direct Linux attachment links.
Linux application-menu naming is OpenCam; Beta remains in build information and
all Linux download filenames. Linux source/license delivery is not optional.

## Compact v1.3.0 release assets

Publish exactly five user binaries (Windows setup/portable, macOS DMG, Ubuntu
Beta deb/archive), one `OpenCam_v<version>_CorrespondingSource.tar.gz` and one
`OpenCam_v<version>_SHA256SUMS.txt`. The source bundle retains the original OBS,
Windows media, macOS media and Linux corresponding-source archives, together
with their provenance manifests, recipes, licenses and the original Linux
checksum list. Component names mentioned in packaged notices are members of
this bundle, not additional top-level downloads. Unpack it, then unpack the
relevant component archive. GitHub's automatically generated OpenCam source
downloads are supplementary and do not replace third-party source delivery.

Validate all source inputs before consolidation and verify the resulting bundle
and all five binaries against the common checksum list after upload. Do not
upload logs or loose internal JSON/TXT using directory globs. Preserve historical
release binary/source URLs; metadata cleanup requires byte-for-byte backups,
reference inspection and verified replacement access first.
