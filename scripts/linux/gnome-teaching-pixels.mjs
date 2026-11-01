// SPDX-License-Identifier: AGPL-3.0-or-later
// Decoder-independent checks used against actual OBS frames, not screenshots.
export function inspectPointerFrames(frames,{width,height,art,artWidth,artHeight,hotspotX,hotspotY,positions}) {
    const stride=width*height*3;
    if(!Number.isSafeInteger(stride)||stride<=0||frames.length%stride||art.length!==artWidth*artHeight*4)
        throw new Error('invalid_frame_geometry');
    let magentaPixels=0,redPixels=0;
    const found=new Set();
    for(let offset=0;offset<frames.length;offset+=stride){
        for(let i=offset;i<offset+stride;i+=3){
            const r=frames[i],g=frames[i+1],b=frames[i+2];
            if(r>180&&g<65&&b>180)magentaPixels++;
            if(r>170&&g<90&&b<100)redPixels++;
        }
        for(let p=0;p<positions.length;p++){
            const left=Math.round(positions[p][0]-hotspotX),top=Math.round(positions[p][1]-hotspotY);
            let compared=0,matched=0;
            for(let y=0;y<artHeight;y+=2)for(let x=0;x<artWidth;x+=2){
                const a=(y*artWidth+x)*4;
                if(art[a+3]<240||left+x<0||top+y<0||left+x>=width||top+y>=height)continue;
                const i=offset+((top+y)*width+left+x)*3;
                compared++;
                if(Math.abs(frames[i]-art[a])+Math.abs(frames[i+1]-art[a+1])+Math.abs(frames[i+2]-art[a+2])<95)matched++;
            }
            if(compared&&matched/compared>.75)found.add(p);
        }
    }
    return {frames:frames.length/stride,positions:[...found].sort(),magentaPixels,redPixels};
}
