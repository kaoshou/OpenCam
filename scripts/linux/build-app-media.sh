#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
if [[ $# -ne 4 || "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
  echo 'Requires Linux x86_64: build-app-media.sh ABS_PINNED_SOURCE ABS_NEW_BUILD ABS_NEW_PREFIX ABS_NEW_EVIDENCE' >&2
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
# Media/preflight tools only. LinuxFFmpegProvider rejects every video-recording fallback.
(
  cd "$build_directory"
  "$source_directory/configure" --prefix="$prefix_directory" --libdir="$prefix_directory/lib" \
    --disable-doc --disable-debug --disable-static --enable-shared --disable-ffplay --disable-autodetect \
    --enable-gpl --enable-libx264 --enable-libpulse --enable-zlib --enable-pthreads --disable-network \
    --disable-everything --enable-ffmpeg --enable-ffprobe --enable-protocol=file,pipe,fd \
    --enable-demuxer=matroska,mov,avi,ogg,mp3,wav,gif,image2,flac,concat,h264,pcm_f32le,pcm_s16le,rawvideo,png_pipe \
    --enable-muxer=matroska,mp4,gif,null,rawvideo,pcm_f32le,pcm_s16le,wav,framehash,h264,image2pipe \
    --enable-parser=h264,hevc,vp8,vp9,av1,mpeg4video,mpegaudio,opus,aac,png \
    --enable-bsf=h264_mp4toannexb,hevc_mp4toannexb,aac_adtstoasc,extract_extradata,filter_units \
    --enable-decoder=h264,hevc,vp8,vp9,av1,mpeg4,mpeg2video,aac,opus,vorbis,mp3,pcm_s16le,pcm_f32le,flac,gif,png,mjpeg,rawvideo,wrapped_avframe \
    --enable-encoder=libx264,aac,gif,rawvideo,pcm_s16le,pcm_f32le,png,wrapped_avframe \
    --enable-filters --enable-indev=pulse,lavfi --enable-outdev=pulse --enable-swscale --enable-swresample \
    > "$evidence_directory/configure.log" 2>&1
  make -j2 > "$evidence_directory/compile.log" 2>&1
  make install > "$evidence_directory/install.log" 2>&1
  cp config.h config_components.h ffbuild/config.mak "$evidence_directory/"
)
git -C "$source_directory" diff --exit-code
