#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
# Called only after application integration, relocation and media validation gates.
[[ $# == 5 ]] || exit 2
package_directory=$1
artifact_directory=$2
obs_source=$3
ffmpeg_source=$4
tested_revision=$5
[[ "$tested_revision" =~ ^[a-f0-9]{40}$ && "$(git rev-parse HEAD)" == "$tested_revision" ]] || exit 2
[[ -d "$package_directory" && ! -e "$artifact_directory" ]] || exit 2
test "$(git -C "$obs_source" rev-parse HEAD)" = 72e86655715d9a25d69ce446aa4dd44ba58e10d2
test "$(git -C "$ffmpeg_source" rev-parse HEAD)" = 1041abdc962f4cc4f394aa8de9dc5236c0c3b9e7
mkdir "$artifact_directory" "$package_directory/linux-notices"
cp LICENSE NOTICE.md THIRD-PARTY-NOTICES.md "$package_directory/"
cp -R third-party/licenses "$package_directory/linux-notices/project-licenses"
cp "$obs_source/COPYING" "$package_directory/linux-notices/OBS-COPYING.txt"
cp "$obs_source/AUTHORS" "$package_directory/linux-notices/OBS-AUTHORS.txt"
mkdir "$package_directory/linux-notices/obs-embedded"
cp "$obs_source/deps/json11/LICENSE.txt" "$package_directory/linux-notices/obs-embedded/json11-LICENSE.txt"
cp "$obs_source/deps/libcaption/LICENSE.txt" "$package_directory/linux-notices/obs-embedded/libcaption-LICENSE.txt"
cp "$obs_source/deps/blake2/LICENSE.blake2" "$package_directory/linux-notices/obs-embedded/BLAKE2-LICENSE.txt"
cp "$obs_source/deps/glad/include/KHR/khrplatform.h" "$package_directory/linux-notices/obs-embedded/Khronos-platform-header.txt"
cp "$obs_source/deps/glad/include/EGL/eglplatform.h" "$package_directory/linux-notices/obs-embedded/EGL-platform-header.txt"
# These installed headers are compiled into libobs, unlike separately supplied
# OS shared libraries. Retain Ubuntu's exact installed copyright notices.
cp /usr/share/doc/libsimde-dev/copyright "$package_directory/linux-notices/obs-embedded/SIMDe-copyright.txt"
cp /usr/share/doc/uthash-dev/copyright "$package_directory/linux-notices/obs-embedded/uthash-copyright.txt"
cp "$ffmpeg_source/COPYING.GPLv2" "$ffmpeg_source/COPYING.GPLv3" "$ffmpeg_source/COPYING.LGPLv2.1" "$package_directory/linux-notices/"
runtime_license=$(find "${NUGET_PACKAGES:-$HOME/.nuget/packages}/microsoft.netcore.app.runtime.linux-x64" -maxdepth 2 -iname LICENSE.TXT -print -quit)
runtime_notices=$(find "${NUGET_PACKAGES:-$HOME/.nuget/packages}/microsoft.netcore.app.runtime.linux-x64" -maxdepth 2 -iname THIRD-PARTY-NOTICES.TXT -print -quit)
test -n "$runtime_license" && test -n "$runtime_notices"
cp "$runtime_license" "$package_directory/linux-notices/DOTNET-LICENSE.txt"
cp "$runtime_notices" "$package_directory/linux-notices/DOTNET-THIRD-PARTY-NOTICES.txt"
printf 'OpenCam 1.2.0 Ubuntu development test\nCommit: %s\nOBS: 72e86655715d9a25d69ce446aa4dd44ba58e10d2\nFFmpeg: 1041abdc962f4cc4f394aa8de9dc5236c0c3b9e7\nSource archives and build scripts accompany this package. OS libraries (including x264, PulseAudio, PipeWire, Mesa) are supplied by Ubuntu, not redistributed here.\n' "$tested_revision" > "$package_directory/SOURCE.txt"
source_directory="$artifact_directory/corresponding-source"
mkdir "$source_directory"
git archive --format=tar.gz -o "$source_directory/OpenCam-$tested_revision.tar.gz" "$tested_revision"
git -C "$obs_source" archive --format=tar.gz -o "$source_directory/OBS-72e8665.tar.gz" HEAD
git -C "$ffmpeg_source" archive --format=tar.gz -o "$source_directory/FFmpeg-1041abd.tar.gz" HEAD
cp "$package_directory/SOURCE.txt" "$source_directory/"
tar -czf "$artifact_directory/OpenCam_v1.2.0_Ubuntu24.04_x64_Test_${tested_revision:0:7}.tar.gz" -C "$package_directory" .
tar -czf "$artifact_directory/OpenCam_v1.2.0_Linux_CorrespondingSource_${tested_revision:0:7}.tar.gz" -C "$source_directory" .
(cd "$artifact_directory" && sha256sum ./*.tar.gz > SHA256SUMS.txt)
