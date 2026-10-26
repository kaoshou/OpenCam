// SPDX-License-Identifier: AGPL-3.0-or-later
import {createReadStream} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync,spawn} from 'node:child_process';
import {basename,resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
export function assertImmutableAsset(local,remote){
  if(!/^[a-f0-9]{64}$/.test(local)||!/^sha256:[a-f0-9]{64}$/.test(remote))throw Error('Invalid asset digest');
  if(remote!=='sha256:'+local)throw Error('Existing Release asset has different bytes; refusing replacement');
}
export function releaseApiPath(repo,apiUrl){
  const prefix=`https://api.github.com/repos/${repo}/releases/`;
  if(typeof apiUrl!=='string'||!apiUrl.startsWith(prefix)||!/^\d+$/.test(apiUrl.slice(prefix.length)))
    throw Error('Invalid resolved release API identity');
  return `repos/${repo}/releases/${apiUrl.slice(prefix.length)}`;
}
async function sha(stream){const hash=createHash('sha256');for await(const b of stream)hash.update(b);return hash.digest('hex');}
async function remoteHash(repo,id){
  const child=spawn('gh',['api',`repos/${repo}/releases/assets/${id}`,'-H','Accept: application/octet-stream']);
  let error='';child.stderr.on('data',b=>error+=b);
  const done=new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',code=>code===0?resolve():reject(Error(error)))});
  const digest=await sha(child.stdout);await done;return 'sha256:'+digest;
}
export async function uploadReleaseAsset(tag,path,repo=process.env.GH_REPO){
  if(!/^v\d+\.\d+\.\d+$/.test(tag)||!/^[-A-Za-z0-9_.]+\/[-A-Za-z0-9_.]+$/.test(repo??''))throw Error('Invalid release identity');
  const local=await sha(createReadStream(path)),name=basename(path);
  // The public by-tag endpoint can return 404 for a draft, including an owned
  // draft. Let gh resolve it, then use its repository-scoped numeric API ID.
  const apiUrl=execFileSync('gh',['release','view',tag,'--repo',repo,'--json','apiUrl','--jq','.apiUrl'],{encoding:'utf8'}).trim();
  const endpoint=releaseApiPath(repo,apiUrl);
  const release=JSON.parse(execFileSync('gh',['api',endpoint],{encoding:'utf8'}));
  const existing=release.assets.find(asset=>asset.name===name);
  if(existing){
    assertImmutableAsset(local,existing.digest??await remoteHash(repo,existing.id));
    console.log('Verified unchanged existing Release asset: '+name);return;
  }
  execFileSync('gh',['release','upload',tag,path,'--repo',repo],{stdio:'inherit',timeout:900000});
  const after=JSON.parse(execFileSync('gh',['api',endpoint],{encoding:'utf8'}));
  const uploaded=after.assets.find(asset=>asset.name===name);
  if(!uploaded)throw Error('Uploaded asset not listed');
  assertImmutableAsset(local,uploaded.digest??await remoteHash(repo,uploaded.id));
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href)
  await uploadReleaseAsset(process.argv[2],process.argv[3]);
