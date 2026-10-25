// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile,mkdtemp,writeFile,rm} from 'node:fs/promises';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {execFileSync} from 'node:child_process';

test('native CMake normalizes Windows cache paths before creating source targets',async t=>{
  const source=await readFile(new URL('../../native/obs-capture/CMakeLists.txt',import.meta.url),'utf8');
  const block=source.match(/# Normalize externally supplied paths\.[\s\S]*?endforeach\(\)/)?.[0];
  assert.ok(block,'Normalize incoming cache paths before using them in generated targets');
  assert.ok(source.indexOf(block)<source.indexOf('configure_file('));
  const root=await mkdtemp(join(tmpdir(),'opencam-cmake-paths-'));t.after(()=>rm(root,{recursive:true,force:true}));
  const script=join(root,'check.cmake');
  await writeFile(script,block+'\nfile(WRITE "${RESULT}" "${OBS_SOURCE}\\n${OBS_RUNTIME}\\n${FFMPEG_HEADERS}\\n${OBS_IMPORT_LIBS}\\n")\n');
  const result=join(root,'result.txt');
  execFileSync('cmake',['-DOBS_SOURCE=D:\\a\\OpenCam\\obs-source','-DOBS_RUNTIME=C:\\Program Files\\OBS',
    '-DFFMPEG_HEADERS=D:\\a\\ffmpeg','-DOBS_IMPORT_LIBS=D:\\a\\build\\imports','-DRESULT='+result,'-P',script]);
  assert.equal(await readFile(result,'utf8'),'D:/a/OpenCam/obs-source\nC:/Program Files/OBS\nD:/a/ffmpeg\nD:/a/build/imports\n');
});
