#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
if [[ $# -ne 4 ]]; then
  echo 'Usage: build-obs-probe.sh ABS_OBS_SOURCE ABS_NEW_BUILD ABS_NEW_STAGE ABS_NEW_EVIDENCE' >&2
  exit 2
fi
if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
  echo 'This isolated builder requires Linux x86_64; Windows/macOS builders are unchanged.' >&2
  exit 2
fi
obs_source=$1
build_directory=$2
stage_directory=$3
evidence_directory=$4
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
inputs=$(node "$script_directory/obs-source-inputs.mjs" "$obs_source" "$stage_directory" "$evidence_directory" "$build_directory")
mkdir "$build_directory" "$stage_directory" "$evidence_directory"
printf '%s\n' "$inputs" > "$evidence_directory/source-inputs.json"
build_stage=prerequisites
failure() {
  local code=$?
  trap - ERR
  node - "$evidence_directory" "$build_stage" "$code" <<'JS'
const fs=require('node:fs'),path=require('node:path');
fs.writeFileSync(path.join(process.argv[2],'build-result.json'),JSON.stringify({status:'BLOCKED',stage:process.argv[3],exitCode:Number(process.argv[4]),recordingStatus:'NOT_TESTED',binaryDistribution:false},null,2));
JS
  exit "$code"
}
trap failure ERR
uname -a > "$evidence_directory/uname.txt"
cp /etc/os-release "$evidence_directory/os-release.txt"
cmake --version > "$evidence_directory/cmake.txt"
cc --version > "$evidence_directory/compiler.txt"
dpkg-query -W -f='${Package}\t${Version}\n' > "$evidence_directory/packages.txt"
pkg-config --modversion libavformat libavutil libswscale libswresample > "$evidence_directory/ffmpeg-library-versions.txt"
pkg-config --atleast-version=62 libavformat || { echo 'Pinned libobs requires FFmpeg >=8.0 (libavformat >=62); supply isolated pinned Linux dependencies. No system replacement attempted.' >&2; false; }
build_stage=configure
# Core-only feasibility first: these flags cannot enable an alternate recorder.
# Source/plugin integration is a separate verified task, not implied by a core build.
cmake -S "$obs_source" -B "$build_directory/obs" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$stage_directory" \
  -DOBS_VERSION_OVERRIDE=33.0.0 \
  -DCMAKE_INSTALL_LIBDIR=lib -DENABLE_FRONTEND=OFF -DENABLE_SCRIPTING=OFF \
  -DENABLE_CORE_MODULES=OFF -DENABLE_RELOCATABLE=ON \
  > "$evidence_directory/configure.log" 2>&1
build_stage=compile
cmake --build "$build_directory/obs" --parallel 2 > "$evidence_directory/compile.log" 2>&1
build_stage=install
cmake --install "$build_directory/obs" > "$evidence_directory/install.log" 2>&1
node - "$evidence_directory" <<'JS'
const fs=require('node:fs'),path=require('node:path');
fs.writeFileSync(path.join(process.argv[2],'build-result.json'),JSON.stringify({status:'CORE_BUILT',recordingStatus:'NOT_TESTED',binaryDistribution:false},null,2));
JS
