// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;
using ScreenRecorder.Core;

namespace ScreenRecorder.Core.Tests;

public class ProductInfoTests
{
    [Theory]
    [InlineData("win-x64", "Windows x64 Build")]
    [InlineData("osx-arm64", "macOS arm64 Build")]
    [InlineData("linux-x64", "Linux x64 Beta Build")]
    [InlineData("", "Development Build")]
    [InlineData(null, "Development Build")]
    [InlineData("unrecognized-rid", "Development Build")]
    public void PackageDescription_UsesBuildTargetNotHost(string? rid, string expected)
        => Assert.Equal(expected, ProductInfo.FormatBuildDescription(rid));

    [Fact]
    public void AssemblyMetadata_UsesCanonicalProductVersion()
    {
        Assert.Matches(new Regex(@"^[0-9]+\.[0-9]+\.[0-9]+$"), ProductInfo.Version);
        Assert.Equal($"v{ProductInfo.Version}", ProductInfo.DisplayVersion);
    }

    [Fact]
    public void VersionFile_HasStrictNumericSemVerAndOneFinalNewline()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "VERSION"));

        Assert.Matches(new Regex(@"^[0-9]+\.[0-9]+\.[0-9]+\n$"), text);
        Assert.Equal(ProductInfo.Version + "\n", text);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ScreenRecorder.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
