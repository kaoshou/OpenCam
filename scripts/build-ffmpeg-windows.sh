#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
[[ "${MSYSTEM:-}" == UCRT64 ]] || { echo 'Use the MSYS2 UCRT64 build environment' >&2; exit 1; }
[[ "$(gcc -dumpmachine)" == x86_64-w64-mingw32 ]] || exit 1
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
output_input="${1:?output directory required}"
mkdir -p "$output_input"
output_dir="$(cd "$output_input" && pwd)"
[[ ! -e "$output_dir/ffmpeg.exe" && ! -e "$output_dir/ffprobe.exe" ]] || { echo 'Refusing existing tools' >&2; exit 1; }
build_root="$(mktemp -d)"
trap 'rm -rf "$build_root"' EXIT
prefix="$build_root/prefix"
notice="$output_dir/ffmpeg-notices"
mkdir -p "$notice/sources" "$notice/licenses" "$prefix"
cp "$repo_root/scripts/windows-media-sources.lock.json" "$notice/sources/"
cp "$0" "$notice/sources/"
cp "$repo_root/scripts/patches/libvpl-mingw-secure-functions.patch" "$notice/sources/"
while IFS=$'\t' read -r name url digest; do
  curl -fL --retry 3 "$url" -o "$notice/sources/$name.archive"
  printf '%s  %s\n' "$digest" "$notice/sources/$name.archive" | sha256sum -c -
  mkdir "$build_root/$name"
  tar -xf "$notice/sources/$name.archive" --strip-components=1 -C "$build_root/$name"
  find "$build_root/$name" -maxdepth 2 -type f \( -iname 'COPYING*' -o -iname 'LICENSE*' -o -iname 'AUTHORS*' \) \
    -exec cp --parents '{}' "$notice/licenses/" \;
done < <(node -e 'for(const s of require(process.argv[1]).sources)console.log([s.id,s.url,s.sha256].join("\t"))' "$repo_root/scripts/windows-media-sources.lock.json")
jobs="$(nproc)"
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig:$prefix/lib64/pkgconfig"
cmake -S "$build_root/zlib" -B "$build_root/zlib-build" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$prefix" -DCMAKE_INSTALL_LIBDIR=lib \
  -DZLIB_BUILD_SHARED=OFF -DZLIB_BUILD_STATIC=ON -DZLIB_BUILD_TESTING=OFF
cmake --build "$build_root/zlib-build" --parallel "$jobs"
cmake --install "$build_root/zlib-build"
# zlib's Windows static target is libzs.a, but its pkg-config file advertises
# -lz. Supply that name from our own archive, never an MSYS2 shared import lib.
cp "$prefix/lib/libzs.a" "$prefix/lib/libz.a"
pushd "$build_root/x264"
./configure --prefix="$prefix" --host=x86_64-w64-mingw32 --enable-static --disable-cli --enable-win32thread
make -j"$jobs"
make install
popd
meson setup "$build_root/dav1d-build" "$build_root/dav1d" --prefix="$prefix" --libdir=lib \
  --buildtype=release --default-library=static -Denable_tools=false -Denable_tests=false
meson compile -C "$build_root/dav1d-build" -j "$jobs"
meson install -C "$build_root/dav1d-build"
make -C "$build_root/nvcodec" PREFIX="$prefix" install
mkdir -p "$prefix/include/AMF"
cp -R "$build_root/amf/amf/public/include/"* "$prefix/include/AMF/"
patch -p1 -d "$build_root/vpl" < "$repo_root/scripts/patches/libvpl-mingw-secure-functions.patch"
cmake -S "$build_root/vpl" -B "$build_root/vpl-build" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$prefix" -DCMAKE_INSTALL_LIBDIR=lib \
  -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTS=OFF -DBUILD_EXAMPLES=OFF \
  -DCMAKE_CXX_FLAGS=-static-libstdc++
cmake --build "$build_root/vpl-build" --parallel "$jobs"
cmake --install "$build_root/vpl-build"
pushd "$build_root/ffmpeg"
./configure --prefix="$build_root/install" --arch=x86_64 --target-os=mingw32 --cc=gcc --cxx=g++ \
  --enable-static --disable-shared --disable-autodetect --disable-network --disable-debug --disable-doc --disable-ffplay \
  --enable-gpl --enable-zlib --enable-libx264 --enable-libdav1d --enable-ffnvcodec --enable-nvenc --enable-amf --enable-libvpl \
  --pkg-config-flags=--static --extra-cflags="-I$prefix/include" \
  --extra-ldflags="-L$prefix/lib -static -static-libgcc -static-libstdc++" --extra-libs=-lstdc++
make -j"$jobs" ffmpeg.exe ffprobe.exe
cp ffmpeg.exe ffprobe.exe "$output_dir/"
cp config.h ffbuild/config.log ffbuild/config.mak "$notice/"
popd
for executable in ffmpeg ffprobe; do
  "$output_dir/$executable.exe" -hide_banner -L > "$notice/$executable-license.txt" 2>&1
  "$output_dir/$executable.exe" -version > "$notice/$executable-version.txt" 2>&1
  objdump -p "$output_dir/$executable.exe" > "$notice/$executable-imports.txt"
  if grep -Eiq 'DLL Name:.*(libgcc|libstdc\+\+|libwinpthread|libx264|libdav1d|libvpl|vpl\.dll|msys-)' "$notice/$executable-imports.txt"; then
    echo 'Unexpected external media-tool runtime dependency' >&2; exit 1
  fi
done
# GCC 16 split the former gcc-libs package into runtime packages. Query the
# installed manifests rather than assuming an obsolete license directory.
for package in libgcc libstdc++ libwinpthread headers crt; do
  while IFS= read -r license; do
    cp --parents "$license" "$notice/licenses/"
  done < <(pacman -Ql "mingw-w64-ucrt-x86_64-$package" | awk '$2 ~ /\/share\/licenses\/.+[^\/]$/ {print $2}')
done
find "$notice/licenses" -type f -name COPYING.RUNTIME | grep -q . || { echo 'Missing GCC Runtime Library Exception' >&2; exit 1; }
pacman -Q > "$notice/build-packages.txt"
gcc --version > "$notice/compiler.txt"
printf 'OpenCam-owned Windows FFmpeg build. Source archives, recipe and VPL MinGW compatibility patch: sources/.\nGCC runtime libraries use their bundled Runtime Library Exception; Windows system libraries are not redistributed.\n' > "$notice/SOURCE-STATUS.txt"
node "$repo_root/scripts/check-owned-media-tools.mjs" "$output_dir" win-x64
printf 'Built self-contained Windows media tools in %s\n' "$output_dir"
