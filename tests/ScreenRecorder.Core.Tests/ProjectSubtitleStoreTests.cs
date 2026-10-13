// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectSubtitleStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("OpenCam-subtitles-").FullName;

    [Fact]
    public async Task IdenticalOriginalNamesNeverOverwriteProjectAssets()
    {
        await using var project = await new JsonProjectStore().CreateAsync(_root, "教材");
        var a = Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\n甲");
        var b = Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nEnglish");
        await File.WriteAllBytesAsync(Path.Combine(_root, "a.srt"), a);
        await File.WriteAllBytesAsync(Path.Combine(_root, "b.srt"), b);
        await using var inputA = File.OpenRead(Path.Combine(_root, "a.srt"));
        await using var inputB = File.OpenRead(Path.Combine(_root, "b.srt"));
        var trackA = await ProjectSubtitleStore.ImportAsync(project, inputA, "same.srt", "zh-TW");
        var trackB = await ProjectSubtitleStore.ImportAsync(project, inputB, "same.srt", "en");
        Assert.NotEqual(trackA.RelativePath, trackB.RelativePath);
        Assert.Equal("same.srt", trackA.OriginalName);
        Assert.Equal("same.srt", trackB.OriginalName);
        await using var savedA = ProjectSubtitleStore.OpenRead(project, trackA);
        await using var savedB = ProjectSubtitleStore.OpenRead(project, trackB);
        using var readA = new MemoryStream(); using var readB = new MemoryStream();
        await savedA.CopyToAsync(readA); await savedB.CopyToAsync(readB);
        Assert.Equal(a, readA.ToArray()); Assert.Equal(b, readB.ToArray());
        Assert.Equal(a, await File.ReadAllBytesAsync(Path.Combine(_root, "a.srt")));
        Assert.Equal(0, project.Current.Revision); // Import does not silently save edits.
    }

    [Fact]
    public async Task MalformedImportDoesNotLeaveAProjectAsset()
    {
        await using var project = await new JsonProjectStore().CreateAsync(_root, "教材");
        var source = Path.Combine(_root, "bad.srt");
        await File.WriteAllTextAsync(source, "not a subtitle");
        await using var input = File.OpenRead(source);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectSubtitleStore.ImportAsync(project, input, "bad.srt", "en"));
        var dir = Path.Combine(project.ProjectDirectory, "subtitles");
        Assert.True(!Directory.Exists(dir) || Directory.GetFiles(dir).Length == 0);
    }

    [Fact]
    public async Task SavedProjectAndRecoveryDraftKeepSubtitleAssetsAfterExternalSourceRemoval()
    {
        var store = new JsonProjectStore();
        var file = Path.Combine(_root, "source.srt");
        await File.WriteAllTextAsync(file, "1\n00:00:01,000 --> 00:00:02,000\n教材");
        string manifest;
        ProjectSubtitleTrack track;
        await using (var handle = await store.CreateAsync(_root, "教材"))
        {
            manifest = Path.Combine(handle.ProjectDirectory, "project.opencam");
            await using (var input = File.OpenRead(file)) track = await ProjectSubtitleStore.ImportAsync(handle, input, "source.srt", "zh-TW");
            track = track with { TimelineSignature = new string('A', 64) };
            var subtitles = new ProjectSubtitleSettings([track], track.Id);
            await handle.SaveDraftAsync(new(Guid.NewGuid(), handle.Current.ProjectId, 0, 1, "教材", []) { Subtitles = subtitles });
            Assert.Equal(track, Assert.Single((await handle.ReadDraftAsync())!.Subtitles.Tracks));
            Assert.Empty(handle.Current.Subtitles.Tracks);
            await handle.SaveAsync(handle.Current with { Revision = 1, SchemaVersion = 4, Subtitles = subtitles }, 0);
        }
        File.Delete(file);
        await using var reopened = await store.OpenAsync(manifest);
        Assert.Equal(4, reopened.Current.SchemaVersion);
        Assert.Equal(track.Id, reopened.Current.Subtitles.DefaultTrackId);
        using var saved = ProjectSubtitleStore.OpenRead(reopened, Assert.Single(reopened.Current.Subtitles.Tracks));
        Assert.True(saved.Length > 0);
    }

    [Fact]
    public void SubtitleChangesAreDirtyAndUndoableWithoutDowngradingSchema()
    {
        var project = new RecordingProject { ProjectId = Guid.NewGuid(), Name = "教材" };
        var track = new ProjectSubtitleTrack(Guid.NewGuid(), "a.srt", "zh-TW", "", 1, new string('A', 64), new string('A', 64));
        track = track with { RelativePath = "subtitles/" + track.Id.ToString("N") + ".srt" };
        var history = new ProjectEditHistory(project);
        history.SetSubtitles(new([track], track.Id));
        Assert.True(new ProjectEditSaveState(project).IsDirty(history.Current));
        Assert.Single(history.Current.Subtitles.Tracks);
        history.Undo();
        Assert.Empty(history.Current.Subtitles.Tracks);
        Assert.False(new ProjectEditSaveState(project).IsDirty(history.Current));
        history.Redo();
        Assert.Equal(track, Assert.Single(history.Current.Subtitles.Tracks));
        Assert.Equal(4, history.Current.SchemaVersion);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
