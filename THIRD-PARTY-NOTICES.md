# Third-party notices / 第三方授權聲明

OpenCam's own source is AGPL-3.0-or-later. That license does **not** replace the licenses below. Original license texts and upstream notices are retained in `third-party/licenses/`; do not translate or remove their attribution. This document is an index, not a replacement for those texts.

OpenCam 自有原始碼的 AGPL 授權不會取代第三方授權。本文件為索引；完整版權、授權及免責文字保留於 `third-party/licenses/`。Windows 位於安裝／解壓目錄，macOS 位於 App 的 `Contents/Resources`。實際封裝版本與檔案雜湊記於 `third-party/manifest.json`。

## Managed libraries, native graphics and fonts

| Component / 元件 | Reviewed version | Attribution and license files |
| --- | --- | --- |
| Avalonia UI and its runtime packages | 11.2.5 | The AvaloniaUI Project; upstream copyright AvaloniaUI OÜ. MIT: `Avalonia-LICENSE.md`; inherited material: `Avalonia-NOTICE.md` |
| CommunityToolkit.Mvvm | 8.4.2 | .NET Foundation and Contributors; MIT: `CommunityToolkit-LICENSE.md` |
| Serilog | 4.4.0 | Serilog Contributors; Apache-2.0: `Serilog-LICENSE.txt` |
| Serilog.Sinks.Console / File | 6.1.1 / 7.0.0 | Respective Serilog contributors; Apache-2.0: `Serilog-Console-LICENSE.txt`, `Serilog-File-LICENSE.txt` |
| NAudio.Core / Wasapi | 2.2.1 | Mark Heath; MIT: `NAudio-LICENSE.txt`; Windows audio implementation |
| MicroCom.Runtime | 0.11.0 | Nikita Tsukanov; MIT: `MicroCom-LICENSE.txt` |
| Tmds.DBus.Protocol | 0.21.3 | Tom Deseyn; MIT: `Tmds-DBus-LICENSE.txt`; D-Bus integration is platform-dependent |
| Microsoft.Extensions.DependencyInjection / Abstractions, Microsoft.Win32.SystemEvents | 10.0.12 | Microsoft; MIT: `Microsoft-LICENSE.txt` |
| System.IO.Pipelines | 8.0.0 | Microsoft; MIT: `Microsoft-LICENSE.txt` |
| SkiaSharp / native assets | 2.88.9 | Microsoft managed wrapper: `SkiaSharp-LICENSE.txt`; native code and dependencies: `Skia-HarfBuzz-NOTICES.txt` |
| HarfBuzzSharp / native assets | 7.3.0.3 | Microsoft managed wrapper: `HarfBuzzSharp-LICENSE.txt`; native HarfBuzz uses its own notices, including Old MIT: `Skia-HarfBuzz-NOTICES.txt` |
| Avalonia ANGLE Windows natives | 2.1.22045.20230930 | ANGLE Project Authors; BSD-style terms: `ANGLE-LICENSE.txt`; Windows-specific |
| Inter fonts in Avalonia.Fonts.Inter | 3.019 (git-0a5106e0b) | Copyright 2020 The Inter Project Authors; SIL OFL 1.1: `Inter-LICENSE.txt`. The NuGet package's MIT declaration does not replace the font license. |
| Self-contained .NET Runtime | selected by publish | Exact runtime package's `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` are copied to `third-party/runtime/`; version and hashes are recorded at packaging time. |

NativeAssets packages contain platform-specific assets; inclusion in the resolved dependency graph does not mean every asset is shipped or loaded on every platform. Packaging records the selected RID and relevant runtime graph. Upstream aggregate notices are intentionally retained in full even when some listed optional code is not used. xUnit and Avalonia.BuildServices are test/build tools, not entries in the application runtime summary.

`third-party/catalog.json` records the reviewed dependency versions and license provenance. For sources without a release commit in NuGet metadata (MicroCom/NAudio), the notice records the upstream license revision/tag separately; it is not a claim of a verified binary source revision. License text imports normalize line endings only.

## FFmpeg, ffprobe and libx264

FFmpeg is Copyright (c) the FFmpeg developers; x264 is Copyright (c) the x264 project contributors. FFmpeg/ffprobe run as separate executables. Official build scripts enable GPL components including libx264, so describing these binaries simply as LGPL is incorrect. FFmpeg's upstream explanation is retained in `FFmpeg-LICENSE.md`; GPL texts and x264's COPYING are also supplied. The bundled executables' `-L` and `-version` output is retained in `third-party/ffmpeg/`.

- macOS build inputs: FFmpeg 7.1.2 and x264 commit `b35605ace3ddf7c1a5d67a2eb553f034aef41d55`, with checksum-verified source archives in `scripts/build-ffmpeg-macos-arm64.sh`. The configuration enables GPL and libx264, not `--enable-version3`. Package license output is authoritative for the built executable, not a generic LGPL/GPL label.
- Windows input: the pinned BtbN `ffmpeg-n8.1.3-win64-gpl-8.1.zip` build in `scripts/install-windows-ffmpeg.ps1`. Its enabled external libraries can differ from macOS. Preserve the upstream archive's notices and inspect its actual configuration; the macOS x264 source pin does not describe this Windows binary.

**Release source-provision gate / 發布前原始碼提供檢核：** License/attribution files alone do not establish GPL distribution compliance. Before release, provide the complete corresponding FFmpeg and enabled GPL-library sources, applicable patches and build information using a license-compliant method. The macOS build preserves its downloaded source archives and build script with its notices. For the Windows third-party build, complete corresponding-source coverage still requires verification; an upstream homepage or OpenCam's own `SOURCE.txt` is not a substitute. Until that verification is recorded, do not claim this notice update completes the release licensing audit.

Reference: https://ffmpeg.org/legal.html ; https://github.com/BtbN/FFmpeg-Builds ; https://code.videolan.org/videolan/x264
