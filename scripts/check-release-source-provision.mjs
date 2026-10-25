// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFile, lstat } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { join, resolve, isAbsolute } from 'node:path';
import { pathToFileURL } from 'node:url';

// This validates a recorded source-delivery review, not legal sufficiency.
export async function checkReleaseSources(coverage, artifactRoot = 'release-assets/sources') {
  const version = (await readFile(new URL('../VERSION',import.meta.url),'utf8')).trim();
  if(!coverage){
    const policy=JSON.parse(await readFile(new URL(`../docs/releases/v${version}-source-coverage.json`,import.meta.url),'utf8'));
    if(policy.status!=='policy-approved')throw Error('Release source coverage is pending.');
    coverage=JSON.parse(await readFile(join(artifactRoot,policy.deliveryManifest)));
    if(coverage.reviewedBy!==policy.reviewedBy)throw Error('Wrong source delivery review policy');
  }
  if (coverage.status !== 'reviewed' || coverage.version !== version || !coverage.reviewedBy)
    throw Error('Release source coverage is pending: verify actual corresponding sources and patches before publishing binaries.');
  const required = new Set(['obs-windows','obs-macos','ffmpeg-tools-windows','ffmpeg-tools-macos']);
  if (!Array.isArray(coverage.archives) || !coverage.archives.length) throw Error('Missing source archives');
  for (const archive of coverage.archives) {
    if (!/^[a-zA-Z0-9][a-zA-Z0-9._-]*$/.test(archive.name) || isAbsolute(archive.name) ||
        !/^[a-f0-9]{64}$/.test(archive.sha256) || !Array.isArray(archive.covers)) throw Error('Invalid source archive record');
    const path=join(artifactRoot,archive.name),info=await lstat(path);
    if(!info.isFile()||info.isSymbolicLink()||info.size===0) throw Error('Invalid source archive file');
    const hash=createHash('sha256');
    for await(const chunk of createReadStream(path)) hash.update(chunk);
    if(hash.digest('hex')!==archive.sha256) throw Error('Changed source archive');
    for(const component of archive.covers) required.delete(component);
  }
  if(required.size) throw Error('Incomplete source coverage: '+[...required].join(', '));
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href)
  await checkReleaseSources(undefined,process.argv[2]);
