// SPDX-License-Identifier: AGPL-3.0-or-later
export function disposeAll(operations){
    let confirmed=true;
    for(const operation of operations)try{operation();}catch{confirmed=false;}
    return confirmed;
}
