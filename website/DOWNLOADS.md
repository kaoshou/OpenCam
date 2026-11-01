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
3. Update `downloads.json`, and the explicit published-version expectations in
   `tests/home.test.mjs`. Run the website suite/build and browser layout checks.
4. Deploy the website only after confirming the public attachment URLs.

There is no browser-side API call or automatic OS detection. All three platforms
remain visible, including when visiting from a phone or a different computer.
GitHub serves the files, so browser download preferences still apply.

## Linux Beta

The notes directly below the top download buttons explain Ubuntu 24.04 / 25.04
x64, X11 versus Wayland capability limits and CPU encoding. There is no separate
Ubuntu promotional section. These notes do not imply full desktop parity.
Set `linuxBetaVersion` in `downloads.json` to the published `version` only after
verifying `OpenCam_v<version>_Linux_x64_Beta.deb`,
`OpenCam_v<version>_Linux_x64_Beta.tar.gz`,
`OpenCam_v<version>_Linux_Beta_CorrespondingSource.tar.gz` and
`OpenCam_v<version>_Linux_Beta_SHA256SUMS.txt` exist with verified hashes in that
public Release. An absent flag intentionally hides direct Linux attachment links.
Linux application-menu naming is OpenCam; Beta remains in build information and
all Linux download filenames. Linux source/license delivery is not optional.
