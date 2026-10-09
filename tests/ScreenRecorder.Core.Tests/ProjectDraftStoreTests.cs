using System.Collections.Immutable;
using System.Text.Json;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectDraftStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCamDraftTests-" + Guid.NewGuid().ToString("N"));
    public ProjectDraftStoreTests() => Directory.CreateDirectory(_root);
    private static ProjectEditDraft Draft(IProjectHandle h, long sequence = 1) =>
        new(Guid.NewGuid(), h.Current.ProjectId, h.Current.Revision, sequence, "unsaved", ImmutableArray<ProjectClip>.Empty);

    [Fact]
    public async Task DraftRoundTripsWithoutMediaCopy()
    {
        string path;
        await using (var h = await new JsonProjectStore().CreateAsync(_root, "saved"))
        {
            path = Path.Combine(h.ProjectDirectory, "project.opencam");
            var original = await File.ReadAllBytesAsync(path);
            await h.SaveDraftAsync(Draft(h));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(Path.Combine(h.ProjectDirectory, "sources")));
        }
        await using var reopened = await new JsonProjectStore().OpenAsync(path);
        Assert.Equal("unsaved", (await reopened.ReadDraftAsync())!.Name);
        Assert.Equal("saved", reopened.Current.Name);
    }

    [Fact]
    public async Task StaleDraftCannotOverwriteNewerDraft()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "saved");
        var draft = Draft(h, 2);
        await h.SaveDraftAsync(draft);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.SaveDraftAsync(draft with { Sequence = 1 }));
        Assert.Equal(2, (await h.ReadDraftAsync())!.Sequence);
    }

    [Fact]
    public async Task CommittedDraftIsNotRecoveredAfterCleanupCrash()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "saved");
        var draft = Draft(h);
        await h.SaveDraftAsync(draft);
        await h.SaveAsync(h.Current with { Revision = 1, Name = draft.Name, ResolvedDraftId = draft.Id }, 0);
        Assert.Null(await h.ReadDraftAsync());
    }

    [Fact]
    public async Task DiscardTombstoneSurvivesCleanupFailure()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "saved");
        var draft = Draft(h);
        await h.SaveDraftAsync(draft);
        await h.SaveDraftAsync(draft with { Sequence = 2 });
        await h.DiscardDraftAsync(draft.Id);
        Assert.True(File.Exists(Path.Combine(h.ProjectDirectory, "project.edits.json.bak")));
        Assert.Null(await h.ReadDraftAsync());
        Assert.Equal("saved", h.Current.Name);
    }

    [Fact]
    public async Task CorruptOrForeignDraftCannotReplaceProject()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "saved");
        await Assert.ThrowsAsync<InvalidDataException>(() => h.SaveDraftAsync(Draft(h) with { ProjectId = Guid.NewGuid() }));
        await File.WriteAllTextAsync(Path.Combine(h.ProjectDirectory, "project.edits.json"), "{broken");
        await Assert.ThrowsAnyAsync<Exception>(() => h.ReadDraftAsync());
        Assert.Equal("saved", h.Current.Name);
    }

    [Fact]
    public async Task DraftWriteFailurePreservesPreviousCopy()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "saved");
        var draft = Draft(h);
        await h.SaveDraftAsync(draft);
        Directory.CreateDirectory(Path.Combine(h.ProjectDirectory, "project.edits.json.bak"));
        var error = await Record.ExceptionAsync(() => h.SaveDraftAsync(draft with { Sequence = 2 }));
        // Windows reports access denied for a destination directory; Unix
        // reports an I/O error. Neither may replace the existing draft.
        Assert.True(error is IOException || OperatingSystem.IsWindows() && error is UnauthorizedAccessException,
            $"Expected a rejected directory replacement, got: {error}");
        Assert.Equal(1, (await h.ReadDraftAsync())!.Sequence);
    }

    [Fact]
    public async Task BaseMismatchIsNotSilentlyRecovered()
    {
        await using var h = await new JsonProjectStore().CreateAsync(_root, "saved");
        await h.SaveDraftAsync(Draft(h));
        await h.SaveAsync(h.Current with { Revision = 1, Name = "external save" }, 0);
        await Assert.ThrowsAsync<InvalidDataException>(() => h.ReadDraftAsync());
        Assert.Equal("external save", h.Current.Name);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
