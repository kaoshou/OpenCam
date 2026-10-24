# libobs Recording Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement task-by-task. User opted out of questions and authorized recommended implementation decisions.

**Goal:** Integrate an isolated, timestamp-aware mature recording core without reducing existing functionality.

**Architecture:** Native libobs helper captures/mixes/encodes; mature libavformat streams MKV through stdout into existing exclusive project output. Managed adapter preserves recorder contracts and only selects a validated runtime for supported configurations.

**Tech Stack:** .NET 8, C++17, libobs 33.0.0, matching FFmpeg 8 headers/runtime, existing platform/recorder services.

**Spec:** docs/superpowers/specs/2026-10-25-libobs-recording-design.md

## Global Constraints

- Version 0.4.1; no publish, merge or destructive staging.
- Preserve existing dirty feature changes and original recordings.
- Real MKV to already owned stream; no writing media by pathname in helper.
- No API security/permission bypass, no arbitrary plugins, no fake hardware PASS.

## Review Focus

- A configured microphone must never silently become another device.
- A helper crash must preserve already muxed output and report failure.
- Resume must retain core/capture identity, not switch silently.
- Private paths and audio content must not enter protocol logs.
- Package/runtime validation must reject stale, modified or incomplete native dependencies.

### Task 1: Native helper and actual core acceptance

**Files:** native/obs-capture/{CMakeLists.txt,main.cpp,mux.hpp}; tests/native/obs-capture.test.mjs; scripts/build-obs-capture.sh.
**Interfaces:** consume JSON request v1; produce stdout MKV and stderr OCOBS events from spec.

- [ ] Write executable integration tests for missing helper, invalid input/no output, synthetic changing-frame/AAC output, successful stop and forced-kill recovery; watch RED.
- [ ] Implement pinned module loading, input validation, synthetic clocked source, platform display/audio source adapters, bounded mature mux and safe shutdown.
- [ ] Build against official pinned runtime, run synthetic tests and inspect actual decoded content/timing.
- [ ] Record exact sources, artifact hashes and limitations; do not commit unrelated combined work.

### Task 2: Managed ownership / capability routing

**Files:** src/ScreenRecorder.Media/Capture/Obs*.cs; src/ScreenRecorder.Recorder/Services/RecordingEngineFactory.cs; tests/ScreenRecorder.Media.Tests/Obs*.cs.
**Interfaces:** IScreenRecorderEngine / IBoundRecordingOutput / IRecordingAudioLevelSource; consumes native contract v1.

- [ ] Write RED tests for request validation, runtime digest/capability validation, startup/EOF/STOP/error/output-ownership behavior using real subprocess where possible.
- [ ] Implement managed adapter and conservative routing; preserve legacy engines for unsupported configurations and synthetic existing tests.
- [ ] Verify actual native helper through adapter and run recorder lifecycle regressions.

### Task 3: Distribution and regression evidence

**Files:** scripts/obs runtime packaging tools, native dependency lock/notice material; docs/verification/2026-10-25-libobs-recording.md.
**Interfaces:** runtime.json capability and digest manifest consumed by managed selector.

- [ ] Test package validation against damaged/missing/wrong-platform/runtime files before implementing assembler.
- [ ] Assemble isolated runtime, complete source/license inventory, test native and managed pipeline from staged package.
- [ ] Run .NET full tests/build, relevant web/native regressions; preserve exact logs and honestly mark physical/long-duration acceptance BLOCKED where unavailable.
- [ ] Fresh final code review and evidence-based corrections, leaving source changes unmerged/unpublished.
