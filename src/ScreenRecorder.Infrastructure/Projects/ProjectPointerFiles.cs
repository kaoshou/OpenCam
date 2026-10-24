// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Infrastructure.Projects;

/// <summary>Promotes a stopped session's private sidecar into immutable project-owned media.
/// A missing, damaged or unaligned track stays explicitly incomplete; video is retained.</summary>
public static class ProjectPointerFiles
{
    public static async Task<ProjectPointerTrack> PromoteAsync(IProjectHandle owner, ProjectSource source,
        Func<PointerTrackReader,Task<long>> verifyClock, CancellationToken ct=default)
    {
        var h=JsonProjectStore.Handle(owner);
        await h.Gate.WaitAsync(ct);
        try
        {
            h.EnsureOpen();
            var parts=ProjectPathPolicy.ValidateRelative(source.RelativePath);
            var header=new PointerTrackHeader(h.Current.ProjectId,source.Id,source.Width,source.Height);
            var identity=new PointerTrackSource(source.FileSize,source.Sha256);
            using var destination=h.Root.OpenChild("pointers",true);
            var temporary=$".pointer-{Guid.NewGuid():N}.part";
            var final=$"{source.Id:N}.ocpt";
            var opened=new List<BoundDirectory>();
            bool complete=false, created=false;
            long offset=0;
            string? verifiedDigest=null;
            try
            {
                BoundDirectory? input=null;
                try
                {
                    input=h.Root;
                    foreach(var part in parts[..^1]) { input=input.OpenChild(part); opened.Add(input); }
                    using var reader=PointerTrackReader.Open(input,parts[^1]+".pointer",header,identity);
                    offset=await verifyClock(reader);
                    if(!reader.Complete) throw new InvalidDataException("Clock verification did not consume the track.");
                    verifiedDigest=reader.VerifiedDigest;
                    complete=true;
                }
                catch(Exception e) when(e is IOException or InvalidDataException)
                { complete=false; offset=0; }
                catch(OperationCanceledException) when(!ct.IsCancellationRequested)
                { complete=false; offset=0; } // A probe deadline invalidates metadata, not the sealed video.
                ct.ThrowIfCancellationRequested();
                using(var writer=PointerTrackWriter.Create(destination,temporary,header))
                {
                    created=true;
                    if(complete)
                    {
                        using var reader=PointerTrackReader.Open(input!,parts[^1]+".pointer",header,identity);
                        while(reader.ReadNext() is {} item) { ct.ThrowIfCancellationRequested(); writer.Append(item); }
                        if(!reader.Complete || reader.VerifiedDigest!=verifiedDigest)
                            throw new InvalidDataException("Pointer track changed during promotion.");
                        writer.Seal(identity with { VideoPtsOffsetMicroseconds=offset });
                    }
                    // Header-only file is a durable incomplete receipt, not an invented trajectory.
                }
                long length; string hash;
                using(var file=destination.Read(temporary))
                { length=file.Length; hash=Convert.ToHexString(await SHA256.HashDataAsync(file,ct)); }
                // A previous interrupted manifest commit may already have published identical data.
                bool existing;
                try
                {
                    using var file=destination.Read(final);
                    if(file.Length!=length || Convert.ToHexString(await SHA256.HashDataAsync(file,ct))!=hash)
                        throw new InvalidDataException("Existing pointer media differs; refusing overwrite.");
                    existing=true;
                }
                catch(FileNotFoundException) { existing=false; }
                if(!existing) { destination.PublishVerified(temporary,final); created=false; }
                return new($"pointers/{final}",length,hash,offset,complete);
            }
            finally
            {
                if(created) destination.DeleteOwnedFile(temporary);
                foreach(var directory in opened.AsEnumerable().Reverse()) directory.Dispose();
            }
        }
        finally { h.Gate.Release(); }
    }
}
