// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Settings;

namespace ScreenRecorder.Media.Tests;

public sealed class Mp4ExportPreferencesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mp4-export-prefs-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "mp4-export.json");

    private static Type StoreType => typeof(SettingsService).Assembly.GetType("ScreenRecorder.Infrastructure.Settings.Mp4ExportPreferencesStore")
        ?? throw new Xunit.Sdk.XunitException("MP4 export preferences store is missing");

    private object CreateStore() => Activator.CreateInstance(StoreType, FilePath)!;
    private async Task<object?> Load(object store)
    {
        var task = (Task)StoreType.GetMethod("LoadAsync")!.Invoke(store, [CancellationToken.None])!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    [Fact]
    public async Task MissingPreferencesUseOriginalSettings()
    {
        var options = await Load(CreateStore());
        Assert.Equal(0, Convert.ToInt32(options!.GetType().GetProperty("Quality")!.GetValue(options)));
        Assert.Equal(0, Convert.ToInt32(options.GetType().GetProperty("Resolution")!.GetValue(options)));
    }

    [Theory]
    [InlineData("{\"Quality\":2,\"Resolution\":1}", 2, 1)]
    [InlineData("{\"Quality\":99,\"Resolution\":1}", 0, 0)]
    [InlineData("{\"Quality\":1,\"Resolution\":-1}", 0, 0)]
    [InlineData("null", 0, 0)]
    [InlineData("broken JSON", 0, 0)]
    public async Task LoadsValidChoicesAndRecoversFromInvalidFiles(string json, int quality, int resolution)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, json);
        var options = await Load(CreateStore());
        Assert.Equal(quality, Convert.ToInt32(options!.GetType().GetProperty("Quality")!.GetValue(options)));
        Assert.Equal(resolution, Convert.ToInt32(options.GetType().GetProperty("Resolution")!.GetValue(options)));
    }

    [Fact]
    public async Task OversizedFileReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{\"Quality\":2,\"Resolution\":2,\"extra\":\"" + new string('a', 65536) + "\"}");
        var options = await Load(CreateStore());
        Assert.Equal(0, Convert.ToInt32(options!.GetType().GetProperty("Quality")!.GetValue(options)));
    }

    [Fact]
    public async Task AcceptedChoiceSurvivesNewStoreAndCanceledSavePreservesIt()
    {
        var store = CreateStore();
        var optionsType = typeof(ProjectExportOptions).Assembly.GetType("ScreenRecorder.Core.Projects.Mp4ExportOptions")!;
        var parameters = optionsType.GetConstructors().Single().GetParameters();
        var chosen = Activator.CreateInstance(optionsType, Enum.ToObject(parameters[0].ParameterType, 2), Enum.ToObject(parameters[1].ParameterType, 1))!;
        var save = StoreType.GetMethod("SaveAsync")!;
        await (Task)save.Invoke(store, [chosen, CancellationToken.None])!;
        var loaded = await Load(CreateStore());
        Assert.Equal(chosen, loaded);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var defaults = Activator.CreateInstance(optionsType, Enum.ToObject(parameters[0].ParameterType, 0), Enum.ToObject(parameters[1].ParameterType, 0))!;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await (Task)save.Invoke(store, [defaults, cancellation.Token])!);
        Assert.Equal(chosen, await Load(CreateStore()));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task UnwritableDestinationDoesNotAbortExport()
    {
        Directory.CreateDirectory(FilePath);
        var store = CreateStore();
        await (Task)StoreType.GetMethod("SaveAsync")!.Invoke(store, [new Mp4ExportOptions(), CancellationToken.None])!;
        Assert.Empty(Directory.GetFiles(_directory));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
