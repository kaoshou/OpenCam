#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
source_file="$repo_root/src/ScreenRecorder.Platform.macOS/Native/OpenCamSystemAudio/main.swift"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
helper="$test_dir/OpenCam.SystemAudio"
microphone_source="$repo_root/src/ScreenRecorder.Platform.macOS/Native/OpenCamMicrophone/main.swift"
microphone_helper="$test_dir/OpenCam.Microphone"

if [[ ! -f "$source_file" ]]; then
    echo "Missing native helper source: $source_file" >&2
    exit 1
fi

if [[ ! -f "$microphone_source" ]]; then
    echo "Missing native microphone helper source: $microphone_source" >&2
    exit 1
fi

xcrun swiftc "$source_file" \
    -O \
    -target arm64-apple-macos13.0 \
    -framework ScreenCaptureKit \
    -framework AVFoundation \
    -framework CoreMedia \
    -framework CoreGraphics \
    -o "$helper"

set +e
error_output="$($helper 2>&1)"
exit_code=$?
set -e

if [[ $exit_code -ne 2 ]]; then
    echo "Expected missing arguments to exit 2, got $exit_code" >&2
    exit 1
fi

if [[ "$error_output" != ERROR* ]]; then
    echo "Expected ERROR diagnostic, got: $error_output" >&2
    exit 1
fi

xcrun swiftc "$microphone_source" \
    -O \
    -target arm64-apple-macos13.0 \
    -framework AVFoundation \
    -o "$microphone_helper"

set +e
microphone_error_output="$($microphone_helper 2>&1)"
microphone_exit_code=$?
set -e

if [[ $microphone_exit_code -ne 2 ]]; then
    echo "Expected microphone helper missing arguments to exit 2, got $microphone_exit_code" >&2
    exit 1
fi

if [[ "$microphone_error_output" != ERROR* ]]; then
    echo "Expected microphone helper ERROR diagnostic, got: $microphone_error_output" >&2
    exit 1
fi

echo "OpenCam native audio helper tests passed"
