#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail

output_input="${1:-}"

fail() {
  printf 'FFmpeg build failed: %s\n' "$1" >&2
  exit 1
}

[[ -n "$output_input" ]] || fail "usage: $0 /path/to/output"
[[ "$(uname -s)" == "Darwin" ]] || fail "this build must run on macOS"
[[ "$(uname -m)" == "arm64" ]] || fail "this build must run natively on Apple Silicon"

mkdir -p "$output_input"
output_dir="$(cd "$output_input" && pwd)"
build_root="$(mktemp -d)"
trap 'rm -rf "$build_root"' EXIT

ffmpeg_version="7.1.2"
ffmpeg_sha256="089bc60fb59d6aecc5d994ff530fd0dcb3ee39aa55867849a2bbc4e555f9c304"
x264_commit="b35605ace3ddf7c1a5d67a2eb553f034aef41d55"
x264_sha256="cd71a7515b0e9a012e1ac9b1f8415bebcaf6fc97d4db32286642ac4c0fbe24f9"
deployment_target="13.0"
dependency_prefix="$build_root/dependencies"
parallelism="$(sysctl -n hw.ncpu)"

curl -fL \
  "https://ffmpeg.org/releases/ffmpeg-${ffmpeg_version}.tar.xz" \
  -o "$build_root/ffmpeg.tar.xz"
printf '%s  %s\n' "$ffmpeg_sha256" "$build_root/ffmpeg.tar.xz" | shasum -a 256 -c -

# The VideoLAN archive endpoint can return a 200 HTML challenge to CI runners.
# The pinned GitHub mirror archive has the same SHA-256; keep verifying it below.
curl -fL \
  "https://codeload.github.com/mirror/x264/tar.gz/${x264_commit}" \
  -o "$build_root/x264.tar.gz"
printf '%s  %s\n' "$x264_sha256" "$build_root/x264.tar.gz" | shasum -a 256 -c -

tar -xf "$build_root/ffmpeg.tar.xz" -C "$build_root"
tar -xf "$build_root/x264.tar.gz" -C "$build_root"

export MACOSX_DEPLOYMENT_TARGET="$deployment_target"

pushd "$build_root/x264-${x264_commit}" >/dev/null
./configure \
  --prefix="$dependency_prefix" \
  --host=aarch64-apple-darwin \
  --enable-static \
  --disable-cli \
  --extra-cflags="-mmacosx-version-min=${deployment_target}" \
  --extra-ldflags="-mmacosx-version-min=${deployment_target}"
make -j"$parallelism"
make install
popd >/dev/null

pushd "$build_root/ffmpeg-${ffmpeg_version}" >/dev/null
PKG_CONFIG_PATH="$dependency_prefix/lib/pkgconfig" ./configure \
  --prefix="$build_root/ffmpeg-install" \
  --arch=arm64 \
  --target-os=darwin \
  --cc=clang \
  --enable-static \
  --disable-shared \
  --disable-autodetect \
  --disable-network \
  --disable-debug \
  --disable-doc \
  --disable-ffplay \
  --enable-gpl \
  --enable-libx264 \
  --enable-videotoolbox \
  --enable-audiotoolbox \
  --enable-avfoundation \
  --pkg-config-flags=--static \
  --extra-cflags="-I${dependency_prefix}/include -mmacosx-version-min=${deployment_target}" \
  --extra-ldflags="-L${dependency_prefix}/lib -mmacosx-version-min=${deployment_target}"
make -j"$parallelism" ffmpeg ffprobe
cp ffmpeg ffprobe "$output_dir/"
popd >/dev/null

for executable in ffmpeg ffprobe; do
  description="$(file "$output_dir/$executable")"
  [[ "$description" == *"Mach-O"* && "$description" == *"arm64"* ]] || \
    fail "$executable is not an arm64 Mach-O executable"
  if otool -L "$output_dir/$executable" | grep -Eq '/opt/|/usr/local/'; then
    fail "$executable contains a non-system dynamic library dependency"
  fi
  if ! vtool -show-build "$output_dir/$executable" | grep -q "minos ${deployment_target}"; then
    fail "$executable does not target macOS ${deployment_target}"
  fi
done

notice_dir="$output_dir/ffmpeg-notices"
mkdir -p "$notice_dir/sources"
for executable in ffmpeg ffprobe; do
  "$output_dir/$executable" -hide_banner -L > "$notice_dir/${executable}-license.txt"
  "$output_dir/$executable" -version > "$notice_dir/${executable}-version.txt"
done
# Preserve exact, checksum-verified inputs and reproducible build instructions.
cp "$build_root/ffmpeg.tar.xz" "$notice_dir/sources/ffmpeg-${ffmpeg_version}.tar.xz"
cp "$build_root/x264.tar.gz" "$notice_dir/sources/x264-${x264_commit}.tar.gz"
cp "$0" "$notice_dir/sources/build-ffmpeg-macos-arm64.sh"
cp "$build_root/ffmpeg-${ffmpeg_version}/LICENSE.md" "$notice_dir/FFmpeg-LICENSE.md"
cp "$build_root/x264-${x264_commit}/COPYING" "$notice_dir/x264-COPYING.txt"

printf 'Built self-contained Apple Silicon FFmpeg tools in %s\n' "$output_dir"
