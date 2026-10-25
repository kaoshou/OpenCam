#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Isolated CI acquisition; never installs or launches the OBS application.
set -euo pipefail
[[ "$(uname -s)" == Darwin && "$(uname -m)" == arm64 ]]
destination=${1:?Expected new input directory}
[[ ! -e "$destination" ]]
mkdir -p "$destination"
destination=$(cd "$destination" && pwd)
repository=$(cd "$(dirname "$0")/.." && pwd)
node --input-type=module - "$repository" "$destination" <<'JS'
import {readFileSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {join} from 'node:path';
const [repo,dest]=process.argv.slice(2);
const lock=JSON.parse(readFileSync(join(repo,'scripts/obs-dependencies.lock.json')));
for(const name of ['obs','ffmpeg','simde']) {
  const source=lock.sources[name], path=join(dest, name+'-source');
  execFileSync('git',['init',path],{stdio:'inherit'});
  execFileSync('git',['-C',path,'fetch','--depth=1',source.url,source.revision],{stdio:'inherit'});
  execFileSync('git',['-C',path,'checkout','--detach','FETCH_HEAD'],{stdio:'inherit'});
  if(execFileSync('git',['-C',path,'rev-parse','HEAD'],{encoding:'utf8'}).trim()!==source.revision) throw Error('Source mismatch');
}
const artifact=JSON.parse(readFileSync(join(repo,'third-party/obs-runtime/inventory.json'))).runtimeArtifact;
execFileSync('curl',['--fail','--location','--retry','3',artifact.url,'-o',join(dest,'obs.dmg')],{stdio:'inherit'});
const actual=execFileSync('shasum',['-a','256',join(dest,'obs.dmg')],{encoding:'utf8'}).split(' ')[0];
if(actual!==artifact.sha256) throw Error('OBS archive checksum mismatch');
JS
mkdir "$destination/mount"
hdiutil attach "$destination/obs.dmg" -readonly -nobrowse -mountpoint "$destination/mount"
trap 'hdiutil detach "$destination/mount" || true' EXIT
ditto "$destination/mount/OBS.app" "$destination/OBS.app"
hdiutil detach "$destination/mount"
trap - EXIT
# Generate public headers with the actual Apple Silicon compiler, not Windows headers.
(
  cd "$destination/ffmpeg-source"
  ./configure --disable-all --disable-autodetect --disable-programs --disable-doc --disable-x86asm
)
