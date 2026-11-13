# v1.3.0 publication scope

## Approved scope

Publish OpenCam 1.3.0 for Windows x64, macOS Apple Silicon and Ubuntu x64 Beta. Preserve OBS/libobs NativeOnly recording, existing media/project behavior and user changes. Defer Windows ARM64, RPM, AppImage, Flatpak and additional distribution support. The user approved this reduced scope in conversation.

## Release presentation

Offer five user binaries: Windows setup/portable, macOS DMG, Ubuntu Beta deb/archive. Make source delivery easier to recognize without removing corresponding sources, recipes, patches, license notices or provenance. Keep one top-level SHA-256 list. GitHub's automatic source downloads are not substitutes for the third-party corresponding sources.

## Approaches and proposed execution choice

1. Keep all loose source manifests: least change but does not address release clutter.
2. Delete source archives: rejected; risks incomplete corresponding-source delivery.
3. Recommended: preserve verified source inputs and manifests inside a clearly named consolidated corresponding-source archive for 1.3.0. Verify components before consolidation and verify final archive delivery after upload. Document that original component archives remain inside it. Keep public package source pointers accurate.

For 1.2.1 preserve existing binaries and the four corresponding-source archives at their current URLs. Audit references before consolidating standalone OBS-SOURCES.json, SOURCE-COVERAGE.json, SOURCE-INDEX.txt and the Linux checksum list into a versioned metadata archive. Back up release JSON and every targeted asset; upload and verify replacement access and update release notes before removing only demonstrably redundant originals. If a package requires a fixed metadata URL, retain that asset instead. Do not overwrite existing binaries or move published tags.

## Boundaries and evidence

Ubuntu 24.04 LTS remains the primary Linux test target. Ubuntu 25.04 is a historical dependency/install test, not a security-maintained recommendation. Preserve X11/Wayland differences, CPU-only Linux encoding and manual-validation limits.

Correct stale test instructions, update bilingual README/manuals/site/release notes for actual 1.3.0 features, and advance public download manifest only after all five published binaries are available and verified. No performance percentage, new platform support, physical-device acceptance or Apple notarization claim without evidence.

The November 4 test packages are inputs/evidence, not official release installers; rebuild official packages at the final reviewed commit. Do not include existing unrelated Linux/GNOME working-tree changes.

## Acceptance

Version and artifact-name checks, source-delivery tamper/omission tests, website tests/build, appropriate managed regression and Windows/macOS/Linux packaging gates pass. Verify final tag SHA, release asset hashes/downloads and deployed site. Preserve failures and skipped/manual tests in an evidence report. Stop before publication if any required gate fails.
