// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

test('display-only module registers one display source and gates WGC on D3D11 and Windows 1903', async t => {
  const root = await mkdtemp(join(tmpdir(), 'opencam-display-entry-')); t.after(() => rm(root, {recursive:true,force:true}));
  await mkdir(join(root, 'util/windows'), {recursive:true});
  // The real entry runs against an isolated libobs boundary, with no OS capture or module updater.
  await writeFile(join(root, 'obs-module.h'), `#include <stdbool.h>
#define OBS_DECLARE_MODULE()
#define OBS_MODULE_USE_DEFAULT_LOCALE(a,b)
#define MODULE_EXPORT
#define GS_DEVICE_DIRECT3D_11 1
struct obs_source_info { const char *id; };
void obs_enter_graphics(void); void obs_leave_graphics(void); int gs_get_device_type(void);
void obs_register_source(const struct obs_source_info *source);
`);
  await writeFile(join(root, 'util/windows/win-version.h'), `struct win_version_info {unsigned major,minor,build,revis;};
void get_win_ver(struct win_version_info *v); int win_version_compare(const struct win_version_info *a,const struct win_version_info *b);
`);
  const implementation = fileURLToPath(new URL('../../native/obs-capture/windows-display-module.c', import.meta.url)).replaceAll('\\', '/');
  const harness = join(root, 'entry-test.c');
  await writeFile(harness, `#include <assert.h>
#include <string.h>
#include <stdio.h>
#include "${implementation}"
struct obs_source_info duplicator_capture_info = {"monitor_capture"};
static unsigned build; static int device; static int depth; static int registrations;
void get_win_ver(struct win_version_info *v) { *v=(struct win_version_info){10,0,build,0}; }
int win_version_compare(const struct win_version_info *a,const struct win_version_info *b) {
if(a->major!=b->major)return a->major>b->major?1:-1; if(a->minor!=b->minor)return a->minor>b->minor?1:-1;
if(a->build!=b->build)return a->build>b->build?1:-1; return 0; }
void obs_enter_graphics(void){assert(depth++==0);} void obs_leave_graphics(void){assert(--depth==0);}
int gs_get_device_type(void){assert(depth==1);return device;}
void obs_register_source(const struct obs_source_info *source){assert(depth==0); assert(strcmp(source->id,"monitor_capture")==0);registrations++;}
int main(void){
const unsigned builds[]={18361,18362,26100,26100}; const int devices[]={1,1,1,0}; const bool expected[]={false,true,true,false};
for(unsigned i=0;i<4;i++){build=builds[i];device=devices[i];registrations=0;assert(obs_module_load());assert(registrations==1);assert(depth==0);assert(wgc_supported==expected[i]);assert(graphics_uses_d3d11==(device==1));}
puts("Display-only entry tests passed");return 0;}
`);
  const output = join(root, process.platform === 'win32' ? 'entry-test.exe' : 'entry-test');
  const compiler = process.env.OPENCAM_C_COMPILER ?? (process.platform === 'win32' ? 'cl' : 'cc');
  const args = process.platform === 'win32'
    ? ['/nologo', '/std:c11', '/utf-8', '/W4', `/I${root}`, harness, `/Fe:${output}`]
    : ['-std=c11', '-Wall', '-Wextra', `-I${root}`, harness, '-o', output];
  const compile = spawnSync(compiler, args, {encoding:'utf8',cwd:root});
  assert.equal(compile.status, 0, `${compile.error?.message ?? ''}\n${compile.stdout}\n${compile.stderr}`);
  const run = spawnSync(output, [], {encoding:'utf8'}); assert.equal(run.status, 0, `${run.stdout}\n${run.stderr}`);
  assert.match(run.stdout, /Display-only entry tests passed/);
});
