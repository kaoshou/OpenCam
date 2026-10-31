// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;

namespace ScreenRecorder.Core;

public static class ProductInfo
{
    public static string Version { get; } = ResolveVersion();

    public static string DisplayVersion => $"v{Version}";

    // A referenced RID-agnostic library loses RuntimeIdentifier in SDK builds.
    // The package entry assembly, not this library or the host OS, owns its RID.
    public static string PackageDisplayVersion => $"{DisplayVersion} ({FormatBuildDescription((Assembly.GetEntryAssembly() ?? typeof(ProductInfo).Assembly)
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "OpenCamRuntimeIdentifier")?.Value)})";

    public static string FormatBuildDescription(string? runtimeIdentifier) => runtimeIdentifier switch
    {
        "win-x64" => "Windows x64 Build",
        "osx-arm64" => "macOS arm64 Build",
        "linux-x64" => "Linux x64 Beta Build",
        _ => "Development Build"
    };

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
