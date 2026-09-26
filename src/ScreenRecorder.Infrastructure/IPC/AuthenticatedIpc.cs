// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScreenRecorder.Infrastructure.IPC;

/// <summary>One fresh server nonce per connection; role-separated authenticated frames.</summary>
public static class AuthenticatedIpc
{
    public const int KeySize = 32;
    public const int MaximumFrameBytes = 1024 * 1024;
    public static byte[] CreateKey() => RandomNumberGenerator.GetBytes(KeySize);

    public static byte[] CopyKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize) throw new ArgumentException("IPC requires a 256-bit key.", nameof(key));
        return key.ToArray();
    }

    public static async Task<byte[]> SendChallengeAsync(Stream stream, byte[] key, CancellationToken token)
    {
        var nonce = RandomNumberGenerator.GetBytes(KeySize);
        await stream.WriteAsync(nonce, token);
        await stream.WriteAsync(Tag(key, 0, nonce, []), token);
        await stream.FlushAsync(token);
        return nonce;
    }

    public static async Task<byte[]> ReadChallengeAsync(Stream stream, byte[] key, CancellationToken token)
    {
        var nonce = new byte[KeySize];
        var tag = new byte[KeySize];
        await stream.ReadExactlyAsync(nonce, token);
        await stream.ReadExactlyAsync(tag, token);
        Verify(key, 0, nonce, [], tag);
        return nonce;
    }

    public static async Task WriteAsync<T>(Stream stream, byte[] key, byte[] nonce, bool response, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaximumFrameBytes) throw new InvalidDataException("IPC frame too large.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(bytes, token);
        await stream.WriteAsync(Tag(key, response ? (byte)2 : (byte)1, nonce, bytes), token);
        await stream.FlushAsync(token);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, byte[] key, byte[] nonce, bool response, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size <= 0 || size > MaximumFrameBytes) throw new InvalidDataException("Invalid IPC frame size.");
        var bytes = new byte[size];
        var tag = new byte[KeySize];
        await stream.ReadExactlyAsync(bytes, token);
        await stream.ReadExactlyAsync(tag, token);
        Verify(key, response ? (byte)2 : (byte)1, nonce, bytes, tag);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Empty IPC message.");
    }

    private static byte[] Tag(byte[] key, byte role, byte[] nonce, byte[] bytes)
    {
        using var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        mac.AppendData([role]);
        mac.AppendData(nonce);
        mac.AppendData(bytes);
        return mac.GetHashAndReset();
    }

    private static void Verify(byte[] key, byte role, byte[] nonce, byte[] bytes, byte[] tag)
    {
        if (!CryptographicOperations.FixedTimeEquals(Tag(key, role, nonce, bytes), tag))
            throw new InvalidDataException("IPC authentication failed.");
    }
}
