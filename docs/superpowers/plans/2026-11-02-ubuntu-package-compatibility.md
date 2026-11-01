# Ubuntu Package Compatibility Repair

> **For agentic workers:** Use superpowers:executing-plans for this approved packaging repair, with test-driven-development and verification-before-completion.

**Goal:** Remove the proven ICU installation blocker on Ubuntu 25.04 without changing recording engines or Windows/macOS.

**Architecture:** Keep the Ubuntu 24.04 build baseline and native ABI checks. Express ICU 74/76 as a strictly validated Debian alternative, consumed by both the deb and archive prerequisite helper. Test the identical deb on clean 24.04 and 25.04 images; do not infer Wayland recording acceptance from installation.

**Spec:** User-approved Ubuntu installation repair in this conversation (2026-11-02); existing NativeOnly and recording-safety requirements remain binding.

## Constraints and review focus

- No GLIBC replacement, ABI symlinks, invariant-globalization workaround, recording fallback, Release, or changes to Windows/macOS capture.
- Reject malformed dependency expressions before any package-manager mutation.
- Existing installed ICU alternative must satisfy archive checks without installing the other version.
- Real deb install must resolve all required ELF dependencies before adding test-harness packages.
- Verify an actual application window, not merely an alive launcher process.
- Ubuntu 25.04 is an additional compatibility test, not a promise of all Ubuntu releases or GNOME 48 teaching tools.

## Task 1: Dependency contract

- [ ] Add a regression test asserting the generated deb accepts either ICU 74 or 76, and tests for strict dependency grammar.
- [ ] Run RED, then implement the alternative manifest and archive-helper handling using APT's dependency solver.
- [ ] Run Linux packaging/launcher tests GREEN, retaining all identity, inventory and license checks.

## Task 2: Real package acceptance and delivery

- [ ] Expand clean-install CI to Ubuntu 24.04 and 25.04 using the same artifact, with OS/package version evidence, ELF audit, archive --check, actual visible window, removal/reinstall data preservation.
- [ ] Update package instructions to distinguish build baseline, tested installation platforms, X11 teaching tools and Wayland recording limitations.
- [ ] Run relevant regression tests, review scoped diff, commit/push only this repair to the authorized development branch; build CI artifacts, no Release.
- [ ] Inspect CI results before providing a package. Report physical Wayland/AV acceptance as unverified unless actually performed.
