# Selectable teaching-pointer styles

## Accepted user intent

The user approved all four original designs in the four-style visual preview, chose B Warm Ivory/Orange as the default, and requested Presentation Stick as the second cursor-mode option. Improve readability without a pencil shape, excessive length, new pointer history, or another video transport. Recording audiovisual correctness remains the first priority.

## Design

- Cursor menu order: native system cursor, presentation stick, yellow halo, halo/click ripple, hidden cursor. Preserve existing enum numeric values and stored selections; reorder only the displayed list. No parenthetical descriptions in zh/en/ja names.
- Four styles: B Warm Ivory/Orange (default), A Teal/Silver, C Round Short Stick, D High-Contrast Black/Gold. Canonical vector artwork is the approved local preview `teaching-pointer-four-styles-v1.html`; export crisp transparent PNG assets from those shapes, not from screenshots. No pencil, laser spot or handwriting feature.
- Sizes: Small/Standard/Large, factors0.82/1/1.23. Standard visible extents approximately96px for A/B,76px for C and101px for D, as shown in the preview. Default Standard. All styles anchor the outer leading tip to the cursor, not the image center.
- Under cursor mode, show a compact thumbnail/name style selector and size selector only when Presentation Stick is selected. Keep main-window settings compact; do not place these controls in the per-clip editor.
- Remember style and size independently of cursor mode. Old settings with no new fields use B/Standard. Invalid new values normalize to B/Standard, never prevent recording. Language changes update labels without changing selections.
- Recording locks the controls. Idle or paused sessions may edit them; changes apply to the next segment only. Existing video retains its baked-in pointer. Settings-window saves must not erase the new fields.
- Both platforms consume the same artwork and size metadata. Preload/cache artwork before confirming overlay readiness; move the existing overlay without allocating/re-rasterizing the full artwork on each tick. Recompute bounds to prevent clipping at Large size; retain physical-coordinate handling on Windows and AppKit-coordinate handling on macOS.
- Preserve the existing readiness/fallback contract: only suppress captured native cursor after the chosen decoration is ready. Missing artwork/helper falls back to ordinary cursor; runtime overlay loss warns without restarting video/audio. Stop/pause/terminal failure/dispose clean up decoration ownership.
- Existing legacy pointer sources and their original silver artwork remain readable/editable. New recordings never create editable pointer tracks. No changes to project schema, audio routes, codec selection, capture source or video transport.
- Add zh-TW/en/ja style/size labels and current user-guide copy. Do not change historical Release notes. Version remains0.4.1; no merge/push/Release authorization in this task.

## Approaches

1. Enlarge the supplied photographic image: declined by the user in favor of a clearer original design.
2. Original short teaching baton variants: selected by the user through visual previews; now all four retained with B default.
3. Editable pointer tracks/global OS-cursor replacement: out of scope, preserving the previously accepted normal-capture safety design.

## Validation boundary

Automated checks cover defaults/normalization/persistence, order/localization, shared render geometry/alpha, large-size clipping, readiness/fallback/cleanup, pause/resume and legacy compatibility. Windows/macOS physical capture inclusion, actual A/V quality, mixed-DPI/Retina/full-screen behavior remain explicit device validation; do not infer those from synthetic tests or compilation.
