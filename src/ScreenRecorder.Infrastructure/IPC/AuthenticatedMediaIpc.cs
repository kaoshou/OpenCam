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

    public static async Task WriteAsync(Stream stream, byte[] key, byte[] nonce, ProjectReply reply, CancellationToken ct)
    {
        var pixels = reply.Frame?.Rgba ?? [];
        if (reply.Frame?.Rgba is not null && !reply.Frame.HasValidPixels) throw new InvalidDataException("Invalid media geometry.");
        var metadata = JsonSerializer.SerializeToUtf8Bytes(reply with { Frame = reply.Frame is { } frame ? frame with { Rgba = null } : null });
        if (metadata.Length > MaximumMetadataBytes) throw new InvalidDataException("Media metadata too large.");
        var header = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 1);
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
        if (BinaryPrimitives.ReadInt32LittleEndian(header) != Magic || BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4)) != 1)
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
        if (reply.Frame?.Rgba is not null || (pixelSize != 0 && (reply.Frame is null ||
            !ProjectFrameReply.ValidGeometry(reply.Frame.PixelWidth, reply.Frame.PixelHeight, pixelSize))))
            throw new InvalidDataException("Invalid media geometry.");
        var pixels = new byte[pixelSize];
        await stream.ReadExactlyAsync(pixels, ct);
        await stream.ReadExactlyAsync(tag, ct);
        Verify(tag, Tag(key, nonce, header, metadata, pixels, 1));
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
