# Homepage downloads

The homepages provide three direct attachment links: Windows x64 portable ZIP,
Windows x64 installer EXE and macOS Apple Silicon arm64 DMG. The original GitHub
Releases listing and localized guide remain text links below the buttons.

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
