// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Session;

namespace ScreenRecorder.Infrastructure.Projects;

public sealed record PointerTrackHeader(Guid ProjectId, Guid SourceId, int Width, int Height);
public sealed record PointerTrackSource(long FileSize, string Sha256, long VideoPtsOffsetMicroseconds = 0);

/// <summary>Fixed-size, hash-chained private records; each write has a recoverable boundary.
/// Call from one serialized consumer. No filenames or absolute desktop coordinates are stored.</summary>
public sealed class PointerTrackWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly NativePointerProtocol _protocol;
    private byte[] _chain = new byte[32];
    private long _count;
    private bool _pointer, _frame, _sealed, _faulted;

    private PointerTrackWriter(FileStream stream, PointerTrackHeader header)
    {
        _stream = stream; _protocol = new(header.Width, header.Height);
        Write(PointerTrackFormat.Header(header));
    }

    public static PointerTrackWriter Create(BoundDirectory owner, string leaf, PointerTrackHeader header)
    {
        PointerTrackFormat.Validate(header);
        var stream = owner.CreateNew(leaf);
        try { return new(stream, header); } catch { stream.Dispose(); throw; }
    }

    public void Append(NativePointerEvent item)
    {
        if (_sealed || _faulted) throw new InvalidOperationException("Pointer track is closed or incomplete.");
        try
        {
            var payload = PointerTrackFormat.Event(item);
            _protocol.Parse(PointerTrackFormat.Protocol(item));
            Write(payload); _count++;
            _pointer |= item.Pointer != null; _frame |= item.Frame != null;
        }
        catch { _faulted = true; throw; }
    }

    public void Seal(PointerTrackSource source)
    {
        if (_sealed || _faulted || !_pointer || !_frame) throw new InvalidOperationException("Cannot seal incomplete pointer track.");
        PointerTrackFormat.Validate(source);
        var payload = new byte[65]; payload[0] = 3;
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(1), source.FileSize);
        Convert.FromHexString(source.Sha256).CopyTo(payload, 9);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(41), _count);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(49), source.VideoPtsOffsetMicroseconds);
        Write(payload);
        try { _stream.Flush(true); _sealed = true; }
        catch { _faulted = true; throw; }
    }

    private void Write(byte[] payload)
    {
        try
        {
            if (_stream.Position > PointerTrackFormat.MaxBytes - 97) throw new IOException("Pointer track capacity reached.");
            var digest = PointerTrackFormat.Digest(_chain, payload);
            _stream.Write(payload); _stream.Write(digest); _stream.Flush(); _chain = digest;
        }
        catch { _faulted = true; throw; }
    }
    public void Dispose() => _stream.Dispose(); // disposal alone never marks a partial recording complete
}

/// <summary>Sequential bounded reader. Export must require Complete after consuming the seal.
/// Recovery exposes only a verified prefix and never changes the original file.</summary>
public sealed class PointerTrackReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly PointerTrackSource _source;
    private readonly NativePointerProtocol _protocol;
    private readonly bool _recover;
    private byte[] _chain = new byte[32];
    private long _count;
    private bool _ended, _pointer, _frame;
    public bool Complete { get; private set; }
    public string? VerifiedDigest => Complete ? Convert.ToHexString(_chain) : null;
    public long VerifiedLength { get; private set; }

    public static async Task<PointerTrackReader> OpenVerifiedAsync(IProjectHandle owner, ProjectSource source,
        CancellationToken ct = default)
    {
        var h = JsonProjectStore.Handle(owner);
        await h.Gate.WaitAsync(ct);
        try
        {
            h.EnsureOpen();
            if (!h.Current.Sources.Contains(source) || source.Pointer is not { Complete: true } track ||
                track.RelativePath != $"pointers/{source.Id:N}.ocpt")
                throw new InvalidDataException("Pointer track is incomplete or not owned by this project.");
            using var folder = h.Root.OpenChild("pointers");
            var stream = folder.Read($"{source.Id:N}.ocpt");
            try
            {
                if (stream.Length != track.FileSize || stream.Length > PointerTrackFormat.MaxBytes ||
                    !Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(track.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Pointer track fingerprint changed.");
                stream.Position = 0;
                return new(stream, new(h.Current.ProjectId, source.Id, source.Width, source.Height),
                    new(source.FileSize, source.Sha256, track.VideoPtsOffsetMicroseconds), false);
            }
            catch { stream.Dispose(); throw; }
        }
        finally { h.Gate.Release(); }
    }

    private PointerTrackReader(FileStream stream, PointerTrackHeader header, PointerTrackSource source, bool recover)
    {
        _stream = stream; _source = source; _recover = recover; _protocol = new(header.Width, header.Height);
        var payload = ReadRecord();
        if (payload == null || !payload.AsSpan().SequenceEqual(PointerTrackFormat.Header(header)))
            throw new InvalidDataException("Pointer track owner or geometry mismatch.");
        VerifiedLength = _stream.Position;
    }

    public static PointerTrackReader Open(BoundDirectory owner, string leaf, PointerTrackHeader header,
        PointerTrackSource source, bool recoverPrefix = false)
    {
        PointerTrackFormat.Validate(header); PointerTrackFormat.Validate(source);
        var stream = owner.Read(leaf);
        try
        {
            if (stream.Length > PointerTrackFormat.MaxBytes) throw new InvalidDataException("Pointer track exceeds capacity.");
            return new(stream, header, source, recoverPrefix);
        }
        catch { stream.Dispose(); throw; }
    }

    public NativePointerEvent? ReadNext()
    {
        if (_ended) return null;
        try
        {
            var payload = ReadRecord() ?? throw new InvalidDataException("Pointer track has no complete seal.");
            if (payload[0] == 3)
            {
                if (!_pointer || !_frame || BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(1)) != _source.FileSize ||
                    !payload.AsSpan(9, 32).SequenceEqual(Convert.FromHexString(_source.Sha256)) ||
                    BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(41)) != _count ||
                    BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(49)) != _source.VideoPtsOffsetMicroseconds ||
                    payload.AsSpan(57).IndexOfAnyExcept((byte)0) >= 0 || _stream.ReadByte() != -1)
                    throw new InvalidDataException("Pointer track source or seal mismatch.");
                VerifiedLength = _stream.Position; Complete = true; _ended = true; return null;
            }
            var item = PointerTrackFormat.Decode(payload);
            _protocol.Parse(PointerTrackFormat.Protocol(item));
            _count++; _pointer |= item.Pointer != null; _frame |= item.Frame != null;
            VerifiedLength = _stream.Position; return item;
        }
        catch (InvalidDataException) when (_recover) { _ended = true; return null; }
        catch { _ended = true; throw; }
    }

    private byte[]? ReadRecord()
    {
        var payload = new byte[65]; var digest = new byte[32];
        if (_stream.ReadByte() is var first && first < 0) return null;
        payload[0] = (byte)first;
        try { _stream.ReadExactly(payload.AsSpan(1)); _stream.ReadExactly(digest); }
        catch (EndOfStreamException e) { throw new InvalidDataException("Truncated pointer record.", e); }
        if (!CryptographicOperations.FixedTimeEquals(digest, PointerTrackFormat.Digest(_chain, payload)))
            throw new InvalidDataException("Pointer record checksum mismatch.");
        _chain = digest; return payload;
    }
    public void Dispose() => _stream.Dispose();
}

internal static class PointerTrackFormat
{
    internal const long MaxBytes = 4L * 1024 * 1024 * 1024;
    internal static void Validate(PointerTrackHeader h)
    {
        ArgumentNullException.ThrowIfNull(h);
        if (h.ProjectId == Guid.Empty || h.SourceId == Guid.Empty || h.Width <= 0 || h.Height <= 0 ||
            (long)h.Width * h.Height > 16_777_216) throw new InvalidDataException("Invalid pointer track header.");
    }
    internal static void Validate(PointerTrackSource s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.FileSize <= 0 || s.Sha256 == null || s.Sha256.Length != 64 || !s.Sha256.All(Uri.IsHexDigit) ||
            s.VideoPtsOffsetMicroseconds is < -60_000_000 or > 60_000_000)
            throw new InvalidDataException("Invalid pointer source identity.");
    }
    internal static byte[] Header(PointerTrackHeader h)
    {
        var p = new byte[65]; "OCPT0001"u8.CopyTo(p.AsSpan(1));
        h.ProjectId.TryWriteBytes(p.AsSpan(9, 16)); h.SourceId.TryWriteBytes(p.AsSpan(25, 16));
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(41), h.Width);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(45), h.Height); return p;
    }
    internal static byte[] Digest(byte[] chain, byte[] payload)
    {
        Span<byte> data = stackalloc byte[97]; chain.CopyTo(data); payload.CopyTo(data[32..]);
        return SHA256.HashData(data);
    }
    internal static byte[] Event(NativePointerEvent item)
    {
        if ((item.Pointer == null) == (item.Frame == null)) throw new InvalidDataException("Expected one pointer event.");
        var p = new byte[65];
        if (item.Pointer is { } s)
        {
            if (!double.IsFinite(s.X) || !double.IsFinite(s.Y) || s.X < 0 || s.Y < 0 ||
                s.X > int.MaxValue || s.Y > int.MaxValue || s.X != Math.Truncate(s.X) || s.Y != Math.Truncate(s.Y))
                throw new InvalidDataException("Invalid physical pointer coordinates.");
            p[0] = 1; Put(p, 1, s.HostMicroseconds); Put(p, 9, (long)s.X); Put(p, 17, (long)s.Y);
            Put(p, 25, s.Visible ? 1 : 0); Put(p, 33, s.Buttons);
        }
        else if (item.Frame is { } f)
        {
            if (f.Sequence > long.MaxValue) throw new InvalidDataException("Invalid frame sequence.");
            p[0] = 2; Put(p, 1, f.SourcePtsMicroseconds); Put(p, 9, f.PresentationMicroseconds);
            Put(p, 17, f.AcquisitionMicroseconds); Put(p, 25, (long)f.Sequence);
        }
        return p;
    }
    internal static NativePointerEvent Decode(byte[] p)
    {
        if (p[0] == 1 && p.AsSpan(41).IndexOfAnyExcept((byte)0) < 0 && Get(p,25) is 0 or 1 && Get(p,33) is >= 0 and <= 7)
            return new(new PointerSample(Get(p,1), Get(p,9), Get(p,17), Get(p,25) == 1, (int)Get(p,33)), null);
        if (p[0] == 2 && p.AsSpan(33).IndexOfAnyExcept((byte)0) < 0 && Get(p,25) > 0)
            return new(null, new NativeFrameClock(Get(p,1), Get(p,9), Get(p,17), (ulong)Get(p,25)));
        throw new InvalidDataException("Unknown pointer record.");
    }
    internal static string Protocol(NativePointerEvent item) => item.Pointer is { } p
        ? FormattableString.Invariant($"POINTER v1 {p.HostMicroseconds} {p.X} {p.Y} {(p.Visible ? 1 : 0)} {p.Buttons}")
        : item.Frame is { } f
            ? FormattableString.Invariant($"FRAME_CLOCK v1 {f.SourcePtsMicroseconds} {f.PresentationMicroseconds} {f.AcquisitionMicroseconds} {f.Sequence}")
            : throw new InvalidDataException("Missing pointer record.");
    private static void Put(byte[] p, int at, long n) => BinaryPrimitives.WriteInt64LittleEndian(p.AsSpan(at), n);
    private static long Get(byte[] p, int at) => BinaryPrimitives.ReadInt64LittleEndian(p.AsSpan(at));
}
