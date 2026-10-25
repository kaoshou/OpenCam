// SPDX-License-Identifier: AGPL-3.0-or-later
import { execFileSync } from 'node:child_process';
import { join,resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { mkdtemp,rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';

export function validateMediaCapabilities(list,platform){
  const required={encoders:['libx264','aac','pcm_s16le','gif','png'],decoders:['h264','hevc','vp9','aac','mp3','png'],
    filters:['scale','crop','pad','overlay','fps','trim','setpts','concat','aresample','atrim','asetpts','amix','volume','afade','fade','palettegen','paletteuse','testsrc','sine','color','aformat','anullsrc','showwavespic'],
    formats:['matroska','mov','mp4','image2','rawvideo','s16le','lavfi','null'],protocols:['file','pipe','fd']};
  if(platform==='win-x64'){required.encoders.push('h264_nvenc','h264_qsv','h264_amf');required.decoders.push('libdav1d');}
  for(const [group,names]of Object.entries(required))for(const name of names)
    if(!new RegExp('\\b'+name+'\\b').test(list[group]??''))throw Error(`Missing ${group} capability: ${name}`);
}
export async function checkOwnedMediaTools(root,platform){
  const suffix=platform==='win-x64'?'.exe':'',ffmpeg=join(root,'ffmpeg'+suffix),ffprobe=join(root,'ffprobe'+suffix);
  const run=(tool,args)=>execFileSync(tool,args,{encoding:'utf8',maxBuffer:16*1024*1024,timeout:120000});
  const listings=Object.fromEntries(['encoders','decoders','filters','formats','protocols'].map(name=>[name,run(ffmpeg,['-hide_banner','-'+name])]));
  validateMediaCapabilities(listings,platform);
  const scratch=await mkdtemp(join(tmpdir(),'opencam-media-capabilities-'));
  try{
    const video=join(scratch,'check.mp4'),gif=join(scratch,'check.gif'),png=join(scratch,'check.png');
    run(ffmpeg,['-nostdin','-v','error','-f','lavfi','-i','testsrc=size=128x96:rate=20','-f','lavfi','-i','sine=sample_rate=48000','-t','1','-c:v','libx264','-pix_fmt','yuv420p','-c:a','aac',video]);
    const probe=JSON.parse(run(ffprobe,['-v','error','-show_streams','-of','json',video]));
    if(!probe.streams.some(s=>s.codec_name==='h264')||!probe.streams.some(s=>s.codec_name==='aac'))throw Error('MP4 stream verification failed');
    run(ffmpeg,['-nostdin','-v','error','-i',video,'-vf','fps=10,scale=96:-1','-an',gif]);
    run(ffmpeg,['-nostdin','-v','error','-i',video,'-frames:v','1','-c:v','png',png]);
    run(ffmpeg,['-nostdin','-v','error','-i',png,'-f','null','-']);
    run(ffmpeg,['-nostdin','-v','error','-i',video,'-f','null','-']);
    run(ffmpeg,['-nostdin','-v','error','-i',gif,'-f','null','-']);
  }finally{await rm(scratch,{recursive:true,force:true});}
  console.log('PASS: required media capabilities, H.264/AAC MP4, PNG, GIF and full decoding');
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href)
  await checkOwnedMediaTools(resolve(process.argv[2]),process.argv[3]);
