// SPDX-License-Identifier: AGPL-3.0-or-later
const styles=['WarmIvory','TealSilver','RoundShort','HighContrast'];
const scales={Small:.82,Standard:1,Large:1.23};
export function supportsDesktop(version,session) {
    return typeof version==='string'&&/^46(?:\.\d+)*$/.test(version)&&session==='wayland';
}
export function pointerGeometry(style,size) {
    if(!styles.includes(style)||!Object.hasOwn(scales,size))throw new Error('unsupported_pointer');
    const scale=scales[size];
    return {style,width:Math.ceil(140*scale),height:Math.ceil(120*scale),hotspotX:16*scale,hotspotY:16*scale};
}
export function pointerPlacement(geometry,x,y,bounds) {
    if(![x,y,bounds?.x,bounds?.y,bounds?.width,bounds?.height].every(Number.isFinite)||bounds.width<=0||bounds.height<=0||
        x<bounds.x||y<bounds.y||x>=bounds.x+bounds.width||y>=bounds.y+bounds.height)return null;
    return {x:x-geometry.hotspotX,y:y-geometry.hotspotY};
}
