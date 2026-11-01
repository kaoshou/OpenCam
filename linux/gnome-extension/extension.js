// SPDX-License-Identifier: AGPL-3.0-or-later
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

// Inert until the compositor/capture feasibility gate and session bridge pass.
// Enabling this skeleton must not change cursor, input, settings or recordings.
export default class OpenCamTeachingTools extends Extension {
    enable() {}
    disable() {}
}
