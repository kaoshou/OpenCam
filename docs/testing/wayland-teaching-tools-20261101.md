# GNOME Wayland teaching tools — implementation evidence

Status: feasibility implementation in progress; **not a usable app feature or
release**. App teaching capabilities remain unchanged/gated off on Wayland.
Target is Ubuntu 24.04 GNOME 46, not every Wayland compositor.

## First real compositor/capture result

Commit `f48c86aebf8a78c12091a85907c7e818230ca437`:
[GNOME feasibility run 41267529204](https://github.com/kaoshou/OpenCam/actions/runs/41267529204)
succeeded on GNOME Shell 46.0 / Mutter 46.2, software-rendered virtual Wayland
desktop. It reused the checksum-verified OBS/libobs runtime from product commit
`10e239f98d44cbabac3708e08c857ee8fcbb9787`; no recording core was replaced.

The test used the actual ScreenCast portal chooser, two 640×360 virtual
monitors, original OpenCam pointer artwork, and a generated GTK Wayland surface
whose native cursor is deliberately magenta. No portal permission response was
fabricated. Random compositor marker bits yielded exactly one selected-monitor
match, rather than inferring identity from identical dimensions.

Five actual MKVs were recorded at 20 FPS and completely decoded. The initial
pixel check sampled 22 decoded frames per file at 5 FPS:

| Case | Observed pixels / behavior |
| --- | --- |
| Native cursor baseline | 5,632 magenta pixels; no teaching pointer |
| Teaching pointer | Original-artwork match at all three expected hotspot placements; zero magenta pixels |
| Pointer plus test stroke | Same three matches; zero magenta; 70,400 red stroke pixels |
| Renderer cleanup | Native cursor restored; 5,632 magenta pixels; stroke absent |
| Extension disabled during recording | Native cursor restored; 2,560 magenta pixels; media bytes continued growing |

The test stroke is a known compositor actor, **not evidence that freehand,
eraser, toolbar, shortcuts or the final drawing model are implemented**.
Additional verification extends this to every decoded frame and movement
outside the selected source before integrating session ownership.

Follow-up commit `3af14722653bd8e003255da57356cf7df65f43f8`,
[run 41267844330](https://github.com/kaoshou/OpenCam/actions/runs/41267844330),
also succeeded. All 89 decoded frames in each of six 20-FPS MKVs were checked:
pointer/stroke still had zero magenta pixels and all three expected artwork
placements; stroke had 284,800 red pixels. Moving outside the authorized source
removed the teaching actor and restored desktop cursor visibility. Cleanup
restored 22,784 magenta pixels in the recorded source; disabling the extension
midcapture restored 11,008 and output continued growing. This supersedes the
sampled-only scope for those six cases, not the remaining physical/manual gates.

## Failed runs retained

- `41266753381`: isolated desktop missing Cairo GI converter / XWayland;
  screen selection did not complete. Fixed CI dependencies, not product core.
- `41266900719`, `41267065676`, `41267204732`, `41267370967`: test chooser
  assumptions did not match GTK 4 monitor toggle roles/hierarchy/state. Actual
  action outcomes and GTK 4.14.5 source confirmed toggle active uses AT-SPI
  `PRESSED`, not checkbox `CHECKED`; repeated polling had deselected the monitor.
  `f48c86a` fixed that fixture and the actual capture then passed.

These failures do not establish a product recording defect or a Wayland
feasibility failure. Original failed artifacts are retained, not relabeled PASS.

## Isolation and remaining acceptance

No changes to `src/`, `native/`, Windows/macOS native builders, release workflow
or version were needed for this gate. No extension was installed or enabled on
the user's Mac. Only an isolated CI desktop enables the test-only fixture entry
point; it is not included in the optional product extension staging package.

Remaining: authenticated single-owner session and explicit connection consent,
owner-loss/lock/lease cleanup, actual app integration and source/crop mapping,
full drawing model/toolbar/configurable shortcuts, optional install/removal
package, long recordings and current-product cross-platform regressions.
Physical GNOME pointer devices, mixed DPI, monitor hotplug, real audio and
subjective A/V synchronization are not validated by virtual desktop tests.

## Local script regression

The normal-desktop-permission Node script/CI/player suite at the first gate
reported 193 passed, 3 skipped, 0 failed. The earlier filesystem-sandbox run
reported 192 passed, 1 failed, 3 skipped: existing AppKit bundle initialization
terminated with SIGABRT. No AppKit source changed; the same complete command
passed with desktop access. Neither result is physical Mac recording acceptance.
