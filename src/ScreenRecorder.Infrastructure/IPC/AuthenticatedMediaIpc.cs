// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScreenRecorder.Infrastructure.IPC;

/// <summary>Versioned media-only envelope. Authenticate bounded metadata before allocating pixels.</summary>
public static class AuthenticatedMediaIpc
{
    public const int MaximumPixelBytes = 64 * 1024 * 1024;
    public const int MaximumMetadataBytes = 64 * 1024;
    private const int Magic = 0x4D434F50;

    // Only authenticated, size-bounded pixels receive this budget. A 4K RGBA
    // frame is ~32 MiB: the control channel's two seconds can truncate it on
    // a loaded host. Keep an absolute bound even when the peer never reads.
    internal static TimeSpan ResponseBudget(int pixelBytes) => TimeSpan.FromSeconds(
        2 + Math.Ceiling(Math.Clamp(pixelBytes, 0, MaximumPixelBytes) / (8d * 1024 * 1024)));
    internal static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(12);

    public static async Task WriteAsync(Stream stream, byte[] key, byte[] nonce, ProjectReply reply, CancellationToken ct)
    {
        var subtitle = reply.SubtitleData is not null;
        if (subtitle && (reply.SubtitleTrackId is null || reply.SubtitleTrackId == Guid.Empty || reply.Frame is not null ||
            reply.SubtitleData!.Length is <= 0 or > MaximumPixelBytes))
            throw new InvalidDataException("Invalid subtitle media payload.");
        if (!subtitle && reply.SubtitleTrackId is not null) throw new InvalidDataException("Missing subtitle media payload.");
        var pixels = subtitle ? reply.SubtitleData! : reply.Frame?.Rgba ?? [];
        if (reply.Frame?.Rgba is not null && !reply.Frame.HasValidPixels) throw new InvalidDataException("Invalid media geometry.");
        var metadata = JsonSerializer.SerializeToUtf8Bytes(reply with { SubtitleData = null,
            Frame = reply.Frame is { } frame ? frame with { Rgba = null } : null });
        if (metadata.Length > MaximumMetadataBytes) throw new InvalidDataException("Media metadata too large.");
        var header = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), subtitle ? 2 : 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), metadata.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), pixels.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(metadata, ct);
        await stream.WriteAsync(Tag(key, nonce, header, metadata, [], 0), ct);
        await stream.WriteAsync(pixels, ct);
        await stream.WriteAsync(Tag(key, nonce, header, metadata, pixels, 1), ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<ProjectReply> ReadAsync(Stream stream, byte[] key, byte[] nonce, CancellationToken ct)
    {
        var header = new byte[16];
        await stream.ReadExactlyAsync(header, ct);
        var version = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (BinaryPrimitives.ReadInt32LittleEndian(header) != Magic || version is not (1 or 2))
            throw new InvalidDataException("Preview protocol mismatch. Restart both processes using the same OpenCam version.");
        var metadataSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        var pixelSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12));
        if (metadataSize is <= 0 or > MaximumMetadataBytes || pixelSize is < 0 or > MaximumPixelBytes)
            throw new InvalidDataException("Invalid media length.");
        var metadata = new byte[metadataSize];
        var tag = new byte[32];
        await stream.ReadExactlyAsync(metadata, ct);
        await stream.ReadExactlyAsync(tag, ct);
        Verify(tag, Tag(key, nonce, header, metadata, [], 0));
        var reply = JsonSerializer.Deserialize<ProjectReply>(metadata, new JsonSerializerOptions { MaxDepth = 32 })
            ?? throw new InvalidDataException("Missing media reply.");
        if (reply.SubtitleData is not null || (version == 2 && (reply.SubtitleTrackId is null ||
            reply.SubtitleTrackId == Guid.Empty || reply.Frame is not null || pixelSize == 0)))
            throw new InvalidDataException("Invalid subtitle media metadata.");
        if (version == 1 && (reply.SubtitleTrackId is not null || reply.Frame?.Rgba is not null || (pixelSize != 0 && (reply.Frame is null ||
            !ProjectFrameReply.ValidGeometry(reply.Frame.PixelWidth, reply.Frame.PixelHeight, pixelSize)))))
            throw new InvalidDataException("Invalid media geometry.");
        var pixels = new byte[pixelSize];
        await stream.ReadExactlyAsync(pixels, ct);
        await stream.ReadExactlyAsync(tag, ct);
        Verify(tag, Tag(key, nonce, header, metadata, pixels, 1));
        if (version == 2) return reply with { SubtitleData = pixels };
        return pixelSize == 0 ? reply : reply with { Frame = reply.Frame! with { Rgba = pixels } };
    }

    private static byte[] Tag(byte[] key, byte[] nonce, byte[] header, byte[] metadata, byte[] pixels, byte part)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hmac.AppendData("OpenCam.Media.v1"u8);
        hmac.AppendData([part]); hmac.AppendData(nonce); hmac.AppendData(header);
        hmac.AppendData(metadata); hmac.AppendData(pixels);
        return hmac.GetHashAndReset();
    }
    private static void Verify(byte[] actual, byte[] expected)
    {
        if (!CryptographicOperations.FixedTimeEquals(actual, expected)) throw new InvalidDataException("Media authentication failed.");
    }
}
