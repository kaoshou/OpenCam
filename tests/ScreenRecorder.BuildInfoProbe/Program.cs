// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core;

Console.WriteLine(ProductInfo.PackageDisplayVersion);
return ProductInfo.PackageDisplayVersion == $"v{ProductInfo.Version} (Linux x64 Beta Build)" ? 0 : 1;
