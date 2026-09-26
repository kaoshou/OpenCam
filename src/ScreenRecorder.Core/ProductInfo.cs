// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;

namespace ScreenRecorder.Core;

public static class ProductInfo
{
    public static string Version { get; } = ResolveVersion();

    public static string DisplayVersion => $"v{Version}";

    private static string ResolveVersion()
    {
        var informational = typeof(ProductInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(ProductInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        return informational.Split('+', 2)[0];
    }
}
