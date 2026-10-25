#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail

app_path="${1:-}"

fail() {
  printf 'Bundle verification failed: %s\n' "$1" >&2
  exit 1
}

[[ -n "$app_path" ]] || fail "usage: $0 /path/to/OpenCam.app"
[[ -d "$app_path" ]] || fail "app bundle does not exist: $app_path"

contents="$app_path/Contents"
macos="$contents/MacOS"
resources="$contents/Resources"
plist="$contents/Info.plist"

[[ -f "$plist" ]] || fail "missing Info.plist"
plutil -lint "$plist" >/dev/null || fail "invalid Info.plist"

for executable in OpenCam ffmpeg ffprobe OpenCam.SystemAudio OpenCam.Microphone OpenCam.CursorOverlay; do
  [[ -x "$macos/$executable" ]] || fail "missing executable: Contents/MacOS/$executable"
done

[[ -s "$macos/PresentationPointer.png" ]] || fail "missing presentation pointer image"
for style in WarmIvory TealSilver RoundShort HighContrast; do
  [[ -s "$macos/TeachingPointers/$style.png" ]] || fail "missing teaching pointer: $style"
done

[[ -f "$resources/OpenCam.icns" ]] || fail "missing Resources/OpenCam.icns"
[[ -f "$resources/LICENSE" ]] || fail "missing Resources/LICENSE"
[[ -f "$resources/NOTICE.md" ]] || fail "missing Resources/NOTICE.md"
[[ -f "$resources/SOURCE.txt" ]] || fail "missing Resources/SOURCE.txt"
[[ "$(shasum -a 256 "$resources/LICENSE" | awk '{print $1}')" == "0d96a4ff68ad6d4b6f1f30f713b18d5184912ba8dd389f86aa7710db079abcb0" ]] || \
  fail "Resources/LICENSE is not the official AGPLv3 text"
grep -q 'SPDX-License-Identifier: AGPL-3.0-or-later' "$resources/SOURCE.txt" || \
  fail "SOURCE.txt has the wrong license identifier"
grep -Eq 'Corresponding source: https://github.com/kaoshou/OpenCam/tree/[[:xdigit:]]{40}' "$resources/SOURCE.txt" || \
  fail "SOURCE.txt is missing a source revision"

icon_name="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$plist" 2>/dev/null || true)"
[[ "$icon_name" == "OpenCam.icns" ]] || fail "CFBundleIconFile must be OpenCam.icns"

minimum_version="$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$plist" 2>/dev/null || true)"
[[ "$minimum_version" == "13.0" ]] || fail "LSMinimumSystemVersion must be 13.0"

if [[ "$(/usr/libexec/PlistBuddy -c 'Print :OpenCamNativeOnlyTest' "$plist" 2>/dev/null || true)" == true ]]; then
  [[ ! -e "$macos/OpenCam.ScreenCapture" ]] || fail "native-only bundle contains the legacy screen helper"
  node --input-type=module - "$macos" <<'NODE'
import {readFile,readdir,lstat} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {join} from 'node:path';
import {execFileSync} from 'node:child_process';
const root=join(process.argv[2],'obs-runtime');
const info=JSON.parse(await readFile(join(process.argv[2],'OBS-TEST-INFO.json')));
const m=JSON.parse(await readFile(join(root,'runtime.json')));
if(!info.nativeOnly||info.compatibilityFallback!==false||!info.testOnly||!m.enabled||m.platform!=='osx-arm64'||
  m.protocol!==1||m.obsVersion!=='33.0.0'||m.sourceRevision!=='72e86655715d9a25d69ce446aa4dd44ba58e10d2')throw Error('Wrong native-only runtime');
const found=new Set();
async function check(dir,prefix=''){
  for(const e of await readdir(dir)){
    const p=join(dir,e),name=prefix+e,s=await lstat(p);
    if(s.isSymbolicLink())throw Error('Symlink runtime entry');
    if(s.isDirectory())await check(p,name+'/');
    else if(s.isFile()&&name!=='runtime.json'){
      const b=await readFile(p);
      if(createHash('sha256').update(b).digest('hex')!==m.files[name])throw Error('Changed native runtime file: '+name);
      if(b.length>=4&&b.readUInt32LE(0)===0xfeedfacf){
        if(b.readUInt32LE(4)!==0x100000c)throw Error('Non-arm64 runtime binary');
        const min=execFileSync('vtool',['-show-build',p],{encoding:'utf8'}).match(/minos (\S+)/)?.[1];
        if(!min||Number(min)>13)throw Error('Runtime requires macOS above 13: '+name);
      }
      found.add(name);
    }else if(!s.isFile())throw Error('Unexpected runtime entry');
  }
}
await check(root);
if(!found.has('Contents/MacOS/OpenCam.ObsCapture')||found.size!==Object.keys(m.files).length)throw Error('Incomplete runtime inventory');
console.log(`Verified native-only runtime: ${found.size} unchanged files; arm64/macOS 13.`);
NODE
fi

helper_description="$(file "$macos/OpenCam.SystemAudio")"
[[ "$helper_description" == *"Mach-O"* ]] || fail "system audio helper is not a Mach-O executable"
[[ "$helper_description" == *"arm64"* ]] || fail "system audio helper is not arm64"

microphone_description="$(file "$macos/OpenCam.Microphone")"
[[ "$microphone_description" == *"Mach-O"* ]] || fail "microphone helper is not a Mach-O executable"
[[ "$microphone_description" == *"arm64"* ]] || fail "microphone helper is not arm64"

cursor_description="$(file "$macos/OpenCam.CursorOverlay")"
[[ "$cursor_description" == *"Mach-O"* ]] || fail "cursor helper is not a Mach-O executable"
[[ "$cursor_description" == *"arm64"* ]] || fail "cursor helper is not arm64"

for media_tool in ffmpeg ffprobe; do
  media_description="$(file "$macos/$media_tool")"
  [[ "$media_description" == *"Mach-O"* ]] || fail "$media_tool is not a Mach-O executable"
  [[ "$media_description" == *"arm64"* ]] || fail "$media_tool is not arm64"
  if otool -L "$macos/$media_tool" | grep -Eq '/opt/|/usr/local/'; then
    fail "$media_tool contains a non-system dynamic library dependency"
  fi
  if ! vtool -show-build "$macos/$media_tool" | grep -q 'minos 13.0'; then
    fail "$media_tool must support macOS 13.0"
  fi
done

codesign --verify --deep --strict "$app_path" >/dev/null 2>&1 || fail "code signature verification failed"

printf 'Verified macOS bundle: %s\n' "$app_path"
