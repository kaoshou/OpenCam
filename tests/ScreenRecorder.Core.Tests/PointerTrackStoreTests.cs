// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Core.Projects;
using System.Security.Cryptography;

namespace ScreenRecorder.Core.Tests;

public sealed class PointerTrackStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("OpenCam-pointer-track-").FullName;
    private readonly PointerTrackHeader _header = new(Guid.NewGuid(), Guid.NewGuid(), 1920, 1080);
    private static readonly PointerTrackSource Source = new(321, new string('a', 64));

    private void CreateTrack()
    {
        using var directory = BoundDirectory.Open(_root);
        using var writer = PointerTrackWriter.Create(directory, "segment.pointer", _header);
        writer.Append(new(new PointerSample(8_000_000, 10, 20, true, 0), null));
        writer.Append(new(null, new NativeFrameClock(0, 8_000_000, 7_999_000, 1)));
        writer.Append(new(new PointerSample(8_050_000, 30, 40, true, 1), null));
        writer.Seal(Source);
    }

    [Fact]
    public void StreamingRoundTripRequiresSourceIdentityAndVerifiedSeal()
    {
        CreateTrack();
        using var directory = BoundDirectory.Open(_root);
        using var reader = PointerTrackReader.Open(directory, "segment.pointer", _header, Source);
        Assert.False(reader.Complete);
        Assert.Equal(new PointerSample(8_000_000, 10, 20, true, 0), reader.ReadNext()!.Value.Pointer);
        Assert.Equal(7_999_000, reader.ReadNext()!.Value.Frame!.Value.AcquisitionMicroseconds);
        Assert.Equal(1, reader.ReadNext()!.Value.Pointer!.Value.Buttons);
        Assert.Null(reader.ReadNext()); Assert.True(reader.Complete);
        Assert.Equal(new FileInfo(Path.Combine(_root, "segment.pointer")).Length, reader.VerifiedLength);
    }

    [Fact]
    public void AnotherProjectOrSourceCannotAdoptTrack()
    {
        CreateTrack(); using var directory = BoundDirectory.Open(_root);
        Assert.Throws<InvalidDataException>(() => PointerTrackReader.Open(directory, "segment.pointer", _header with { ProjectId = Guid.NewGuid() }, Source));
        Assert.Throws<InvalidDataException>(() => PointerTrackReader.Open(directory, "segment.pointer", _header with { SourceId = Guid.NewGuid() }, Source));
        using var reader = PointerTrackReader.Open(directory, "segment.pointer", _header, Source with { Sha256 = new string('b', 64) });
        Assert.Throws<InvalidDataException>(() => { while (reader.ReadNext() != null) { } });
        Assert.False(reader.Complete);
    }

    [Fact]
    public void TruncationRecoversOnlyCompleteVerifiedPrefixNeverCompleteExport()
    {
        CreateTrack();
        var path = Path.Combine(_root, "segment.pointer");
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) file.SetLength(file.Length - 12);
        using var directory = BoundDirectory.Open(_root);
        using var reader = PointerTrackReader.Open(directory, "segment.pointer", _header, Source, recoverPrefix: true);
        var count = 0; while (reader.ReadNext() != null) count++;
        Assert.Equal(3, count); Assert.False(reader.Complete);
        Assert.True(reader.VerifiedLength < new FileInfo(path).Length);
        using var strict = PointerTrackReader.Open(directory, "segment.pointer", _header, Source);
        Assert.Throws<InvalidDataException>(() => { while (strict.ReadNext() != null) { } });
    }

    [Fact]
    public void CorruptionAndDataAfterSealAreRejected()
    {
        CreateTrack(); var path = Path.Combine(_root, "segment.pointer");
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 1; File.WriteAllBytes(path, bytes);
        using var directory = BoundDirectory.Open(_root);
        using (var reader = PointerTrackReader.Open(directory, "segment.pointer", _header, Source))
            Assert.Throws<InvalidDataException>(() => { while (reader.ReadNext() != null) { } });
        bytes[^1] ^= 1; File.WriteAllBytes(path, [.. bytes, 42]);
        using var trailing = PointerTrackReader.Open(directory, "segment.pointer", _header, Source);
        Assert.Throws<InvalidDataException>(() => { while (trailing.ReadNext() != null) { } });
        Assert.False(trailing.Complete);
    }

    [Fact]
    public void ExistingAndLinkedFilesAreNotOverwrittenOrRead()
    {
        CreateTrack(); using var directory = BoundDirectory.Open(_root);
        Assert.Throws<IOException>(() => PointerTrackWriter.Create(directory, "segment.pointer", _header));
        Assert.Throws<InvalidDataException>(() => PointerTrackWriter.Create(directory, "../escape.pointer", _header));
        File.CreateSymbolicLink(Path.Combine(_root, "linked.pointer"), Path.Combine(_root, "segment.pointer"));
        if(OperatingSystem.IsWindows())
            Assert.Throws<InvalidDataException>(() => PointerTrackReader.Open(directory, "linked.pointer", _header, Source));
        else
            Assert.ThrowsAny<IOException>(() => PointerTrackReader.Open(directory, "linked.pointer", _header, Source));
    }

    [Fact]
    public void InvalidOrEmptyTrackCannotBeSealed()
    {
        using var directory = BoundDirectory.Open(_root);
        using var writer = PointerTrackWriter.Create(directory, "segment.pointer", _header);
        Assert.Throws<InvalidOperationException>(() => writer.Seal(Source));
        Assert.Throws<InvalidDataException>(() => writer.Append(new(new PointerSample(1, 2000, 0, true, 0), null)));
        Assert.Throws<InvalidOperationException>(() => writer.Append(new(null, null)));
    }

    [Fact]
    public void InvalidSamplePermanentlyPreventsSealingAnApparentlyCompleteTrack()
    {
        using var directory = BoundDirectory.Open(_root);
        using var writer = PointerTrackWriter.Create(directory, "segment.pointer", _header);
        writer.Append(new(new PointerSample(10, 12, 25, true, 0), null));
        writer.Append(new(null, new NativeFrameClock(0, 10, 9, 1)));
        Assert.Throws<InvalidDataException>(() => writer.Append(new(new PointerSample(9, 12, 25, true, 0), null)));
        Assert.Throws<InvalidOperationException>(() => writer.Seal(Source));
    }

    [Fact]
    public void RecoveryHandlesMalformedButtonPayloadEvenWithValidRecordChecksum()
    {
        CreateTrack();
        var path = Path.Combine(_root, "segment.pointer");
        var bytes = File.ReadAllBytes(path);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(97 + 33), long.MaxValue);
        var input = new byte[97];
        bytes.AsSpan(65, 32).CopyTo(input);
        bytes.AsSpan(97, 65).CopyTo(input.AsSpan(32));
        SHA256.HashData(input).CopyTo(bytes, 97 + 65);
        File.WriteAllBytes(path, bytes);
        using var directory = BoundDirectory.Open(_root);
        using var recovery = PointerTrackReader.Open(directory, "segment.pointer", _header, Source, recoverPrefix: true);
        Assert.Null(recovery.ReadNext());
        Assert.False(recovery.Complete);
        Assert.Equal(97, recovery.VerifiedLength);
        using var strict = PointerTrackReader.Open(directory, "segment.pointer", _header, Source);
        Assert.Throws<InvalidDataException>(() => strict.ReadNext());
    }

    [Fact]
    public void VerifiedVideoClockShiftIsBoundIntoSeal()
    {
        CreateTrack(); using var directory = BoundDirectory.Open(_root);
        using var reader = PointerTrackReader.Open(directory, "segment.pointer", _header,
            Source with { VideoPtsOffsetMicroseconds = 21_000 });
        Assert.Throws<InvalidDataException>(() => { while (reader.ReadNext() != null) { } });
    }

    [Fact]
    public async Task ProjectOwnedReaderChecksTrackDigestBeforeExposingSamples()
    {
        await using var project = await new JsonProjectStore().CreateAsync(_root, "owned");
        var id = Guid.NewGuid();
        using var root = BoundDirectory.Open(project.ProjectDirectory);
        using var folder = root.OpenChild("pointers", create: true);
        var header = new PointerTrackHeader(project.Current.ProjectId, id, 1920, 1080);
        using (var writer = PointerTrackWriter.Create(folder, $"{id:N}.ocpt", header))
        {
            writer.Append(new(new PointerSample(10, 12, 25, true, 0), null));
            writer.Append(new(null, new NativeFrameClock(0, 10, 9, 1)));
            writer.Seal(Source);
        }
        long length; string hash;
        using (var file = folder.Read($"{id:N}.ocpt"))
        { length = file.Length; hash = Convert.ToHexString(await SHA256.HashDataAsync(file)); }
        var source = new ProjectSource { Id = id, RelativePath = "sources/video.mkv", SessionId = "s", FileSize = Source.FileSize,
            Sha256 = Source.Sha256, Width = 1920, Height = 1080, VideoCodec = "h264", Timing = new(new(1,1000),0,1000),
            Pointer = new($"pointers/{id:N}.ocpt", length, hash, 0, true) };
        await project.SaveAsync(project.Current with { SchemaVersion = 5, Revision = 1, Sources = [source], Sessions = ["s"] }, 0);
        using (var reader = await PointerTrackReader.OpenVerifiedAsync(project, source))
            Assert.Equal(12, reader.ReadNext()!.Value.Pointer!.Value.X);
        await Assert.ThrowsAsync<InvalidDataException>(() => PointerTrackReader.OpenVerifiedAsync(project, source with { Sha256 = new string('c',64) }));
        // Even if every record is otherwise well-formed, a changed owned file must not be accepted.
        var bytes = File.ReadAllBytes(Path.Combine(project.ProjectDirectory, "pointers", $"{id:N}.ocpt"));
        bytes[^1] ^= 1;
        File.WriteAllBytes(Path.Combine(project.ProjectDirectory, "pointers", $"{id:N}.ocpt"), bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => PointerTrackReader.OpenVerifiedAsync(project, source));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
