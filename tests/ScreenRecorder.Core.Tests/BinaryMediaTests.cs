using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.Core.Tests;

public class BinaryMediaTests
{
    [Fact]
    public async Task EmptyPixelsAreNotSilentlyConvertedToPendingFrame()
    {
        using var stream = new MemoryStream();
        var reply = new ProjectReply(true, null, ProjectSnapshot.Closed) { Frame = new(7, 9, Guid.NewGuid(), []) };
        await Assert.ThrowsAsync<InvalidDataException>(() => AuthenticatedMediaIpc.WriteAsync(stream,
            AuthenticatedIpc.CreateKey(), AuthenticatedIpc.CreateKey(), reply, default));
    }

    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(8)] [InlineData(12)] [InlineData(20)]
    [InlineData(-1)] [InlineData(-40)]
    public async Task BinaryMediaRejectsTampering(int index)
    {
        var key = AuthenticatedIpc.CreateKey(); var nonce = AuthenticatedIpc.CreateKey();
        using var stream = new MemoryStream();
        await AuthenticatedMediaIpc.WriteAsync(stream, key, nonce, new(true, null, ProjectSnapshot.Closed)
            { Frame = new(7, 9, Guid.NewGuid(), new byte[512 * 288 * 4]) }, default);
        var bytes = stream.ToArray(); bytes[index < 0 ? bytes.Length + index : index] ^= 255;
        using var corrupt = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => AuthenticatedMediaIpc.ReadAsync(corrupt, key, nonce, default));
    }

    [Fact]
    public async Task BinaryMediaRejectsWrongKeyNonceTruncationAndCancellation()
    {
        var key = AuthenticatedIpc.CreateKey(); var nonce = AuthenticatedIpc.CreateKey();
        using var stream = new MemoryStream();
        await AuthenticatedMediaIpc.WriteAsync(stream, key, nonce, new(false, "error", ProjectSnapshot.Closed), default);
        var bytes = stream.ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => AuthenticatedMediaIpc.ReadAsync(new MemoryStream(bytes), AuthenticatedIpc.CreateKey(), nonce, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => AuthenticatedMediaIpc.ReadAsync(new MemoryStream(bytes), key, AuthenticatedIpc.CreateKey(), default));
        await Assert.ThrowsAnyAsync<IOException>(() => AuthenticatedMediaIpc.ReadAsync(new MemoryStream(bytes[..^1]), key, nonce, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AuthenticatedMediaIpc.ReadAsync(new MemoryStream(bytes), key, nonce, new(true)));
        var decoded = await AuthenticatedMediaIpc.ReadAsync(new MemoryStream(bytes), key, nonce, default);
        Assert.False(decoded.Success); Assert.Equal("error", decoded.Error); Assert.Null(decoded.Frame);
    }
}
