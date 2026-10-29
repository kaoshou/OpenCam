#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
if [[ $# -ne 4 || "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
  echo 'Requires Linux x86_64: build-ffmpeg-dependency.sh ABS_PINNED_SOURCE ABS_NEW_BUILD ABS_NEW_PREFIX ABS_NEW_EVIDENCE' >&2
  exit 2
fi
source_directory=$1
build_directory=$2
prefix_directory=$3
evidence_directory=$4
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
inputs=$(node --input-type=module - "$script_directory/obs-source-inputs.mjs" "$source_directory" "$build_directory" "$prefix_directory" "$evidence_directory" <<'JS'
import {pathToFileURL} from 'node:url';
const {validateLinuxFfmpegInputs}=await import(pathToFileURL(process.argv[2]));
const result=await validateLinuxFfmpegInputs({sourceRoot:process.argv[3],buildRoot:process.argv[4],stageRoot:process.argv[5],evidenceRoot:process.argv[6]});
console.log(JSON.stringify(result));
JS
)
mkdir "$build_directory" "$prefix_directory" "$evidence_directory"
printf '%s\n' "$inputs" > "$evidence_directory/source-inputs.json"
# Private Linux dependency prefix only; never install into /usr or replace the OS FFmpeg.
# No capture devices or command-line recorder are built by this library prerequisite.
(
  cd "$build_directory"
  "$source_directory/configure" --prefix="$prefix_directory" --libdir="$prefix_directory/lib" \
    --disable-programs --disable-doc --disable-debug --disable-static --enable-shared \
    --disable-everything --enable-protocol=file,pipe --enable-muxer=matroska \
    --enable-demuxer=matroska --enable-encoder=aac --enable-decoder=aac,h264 \
    --enable-parser=aac,h264 --enable-swscale --enable-swresample \
    > "$evidence_directory/configure.log" 2>&1
  make -j2 > "$evidence_directory/compile.log" 2>&1
  make install > "$evidence_directory/install.log" 2>&1
  cp config.h config_components.h ffbuild/config.mak "$evidence_directory/"
)
git -C "$source_directory" diff --exit-code
