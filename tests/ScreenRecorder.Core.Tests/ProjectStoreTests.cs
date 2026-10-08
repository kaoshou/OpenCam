// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Diagnostics;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCamProjectTests-" + Guid.NewGuid().ToString("N"));
    public ProjectStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task SaveReopenAndMovePreserveProjectWithoutMp4()
    {
        string dir;
        var store = new JsonProjectStore();
        await using (var handle = await store.CreateAsync(_root, "教學"))
        {
            dir = handle.ProjectDirectory;
            var next = handle.Current with { Name = "修改名稱", Revision = 1 };
            var receipt = await handle.SaveAsync(next, 0);
            Assert.Equal(1, receipt.Revision);
            Assert.False(handle.NeedsRecoveryConfirmation);
        }
        var moved = Path.Combine(_root, "moved");
        Directory.Move(dir, moved);
        await using var reopened = await new JsonProjectStore().OpenAsync(Path.Combine(moved, "project.opencam"));
        Assert.Equal("修改名稱", reopened.Current.Name);
        Assert.Equal(1, reopened.Current.Revision);
        Assert.Empty(Directory.GetFiles(moved, "*.mp4", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task WriterLeaseRejectsSecondWriterAndReleasesOnClose()
    {
        var store = new JsonProjectStore();
        var h = await store.CreateAsync(_root, "one");
        var path = Path.Combine(h.ProjectDirectory, "project.opencam");
        await Assert.ThrowsAnyAsync<IOException>(() => store.OpenAsync(path));
        await h.DisposeAsync();
        await using var reopened = await store.OpenAsync(path);
        Assert.Equal("one", reopened.Current.Name);
    }

    [Fact]
    public async Task WriterLeaseIsEnforcedAcrossProcesses()
    {
        var store = new JsonProjectStore();
        var h = await store.CreateAsync(_root, "two processes");
        var path = Path.Combine(h.ProjectDirectory, "project.opencam");
        using (var second = StartProbe("open", path))
        {
            await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(4, second.ExitCode);
        }
        await h.DisposeAsync();
        using var third = StartProbe("open", path);
        await third.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, third.ExitCode);
    }

    [Fact]
    public async Task KilledWriterLeavesAReadableCommittedProject()
    {
        var store = new JsonProjectStore();
        var h = await store.CreateAsync(_root, "before crash");
        var path = Path.Combine(h.ProjectDirectory, "project.opencam");
        await h.DisposeAsync();
        using var writer = StartProbe("save-loop", path);
        try
        {
            Assert.Equal("ACQUIRED", await writer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.StartsWith("COMMITTED:", await writer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            writer.Kill(entireProcessTree: true);
            await writer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            if (!writer.HasExited) { writer.Kill(entireProcessTree: true); await writer.WaitForExitAsync(); }
        }
        await using var reopened = await store.OpenAsync(path);
        Assert.False(reopened.NeedsRecoveryConfirmation);
        Assert.True(reopened.Current.Revision >= 1);
        Assert.StartsWith("revision-", reopened.Current.Name);
    }

    private static Process StartProbe(string action, string path)
    {
        var executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "project-probe", "ScreenRecorder.ProjectStoreProbe.dll"));
        info.ArgumentList.Add(action);
        info.ArgumentList.Add(path);
        return Process.Start(info)!;
    }

    [Fact]
    public async Task StaleRevisionAndForeignProjectCannotOverwrite()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "original");
        await h.SaveAsync(h.Current with { Revision = 1, Name = "new" }, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.SaveAsync(h.Current with { Revision = 2 }, 0));
        await Assert.ThrowsAsync<InvalidDataException>(() => h.SaveAsync(h.Current with { Revision = 2, ProjectId = Guid.NewGuid() }, 1));
        Assert.Equal("new", h.Current.Name);
        Assert.Equal(1, h.Current.Revision);
    }

    [Fact]
    public async Task CorruptMainOffersBackupWithoutSilentlyReplacingIt()
    {
        var store = new JsonProjectStore();
        string path;
        await using (var h = await store.CreateAsync(_root, "before"))
        {
            path = Path.Combine(h.ProjectDirectory, "project.opencam");
            await h.SaveAsync(h.Current with { Revision = 1, Name = "after" }, 0);
        }
        await File.WriteAllTextAsync(path, "broken");
        await using var recovered = await store.OpenAsync(path);
        Assert.True(recovered.NeedsRecoveryConfirmation);
        Assert.Equal("before", recovered.Current.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovered.SaveAsync(recovered.Current with { Revision = 1 }, 0));
        Assert.Equal("broken", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FutureSchemaDoesNotFallBackToOldBackup()
    {
        var store = new JsonProjectStore();
        string path;
        RecordingProject project;
        await using (var h = await store.CreateAsync(_root, "future"))
        {
            path = Path.Combine(h.ProjectDirectory, "project.opencam");
            await h.SaveAsync(h.Current with { Revision = 1 }, 0);
            project = h.Current;
        }
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(project with { SchemaVersion = 2 }));
        await Assert.ThrowsAsync<NotSupportedException>(() => store.OpenAsync(path));
        Assert.Contains("\"SchemaVersion\":2", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CancelledSaveKeepsPreviousCommittedRevision()
    {
        var store = new JsonProjectStore();
        string path;
        await using (var h = await store.CreateAsync(_root, "old"))
        {
            path = Path.Combine(h.ProjectDirectory, "project.opencam");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.SaveAsync(
                h.Current with { Name = "uncommitted", Revision = 1 }, 0, new CancellationToken(true)));
            Assert.Equal(0, h.Current.Revision);
        }
        await using var reopened = await store.OpenAsync(path);
        Assert.Equal("old", reopened.Current.Name);
    }

    [Fact]
    public async Task OversizedManifestIsRejectedBeforeDeserialization()
    {
        var store = new JsonProjectStore();
        string path;
        await using (var h = await store.CreateAsync(_root, "bounded"))
            path = Path.Combine(h.ProjectDirectory, "project.opencam");
        await File.WriteAllTextAsync(path, new string(' ', 4 * 1024 * 1024 + 1));
        await Assert.ThrowsAnyAsync<Exception>(() => store.OpenAsync(path));
        Assert.Equal(4 * 1024 * 1024 + 1, new FileInfo(path).Length);
    }

    [Fact]
    public async Task FailedBackupWriteDoesNotAdvanceCurrentOrOverwriteMain()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "before");
        Directory.CreateDirectory(Path.Combine(h.ProjectDirectory, "project.opencam.bak"));
        await Assert.ThrowsAnyAsync<IOException>(() => h.SaveAsync(h.Current with { Revision = 1 }, 0));
        Assert.Equal(0, h.Current.Revision);
        var saved = JsonSerializer.Deserialize<RecordingProject>(await File.ReadAllTextAsync(Path.Combine(h.ProjectDirectory, "project.opencam")));
        Assert.Equal(0, saved!.Revision);
    }

    [Fact]
    public async Task JournalPersistsAndCompletesIdempotently()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "journal");
        var journal = new ProjectWriteJournal();
        var intent = new SegmentCommitIntent(Guid.NewGuid(), Guid.NewGuid(), "session-1",
            "sessions/Sessions/session-1/segment_000.mkv", 0);
        await journal.WriteIntentAsync(h, intent);
        await journal.WriteIntentAsync(h, intent);
        Assert.Single(await journal.ReadPendingAsync(h));
        await Assert.ThrowsAsync<InvalidDataException>(() => journal.WriteIntentAsync(h, intent with { SourceId = Guid.NewGuid() }));
        await journal.CompleteAsync(h, intent.OperationId);
        await journal.CompleteAsync(h, intent.OperationId);
        Assert.Empty(await journal.ReadPendingAsync(h));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
