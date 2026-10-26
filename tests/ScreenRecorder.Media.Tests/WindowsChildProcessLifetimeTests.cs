// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using ScreenRecorder.Platform.Windows;

namespace ScreenRecorder.Media.Tests;

public class WindowsChildProcessLifetimeTests
{
    [Fact]
    public void TerminationDeniedWhileExitingStillWaitsForConfirmedExit()
    {
        // The OS can reject another termination request while completing exit.
        WindowsChildProcessLifetime.StopAndJoin(() => false, () => false,
            milliseconds => { Assert.Equal(5000u, milliseconds); return 0; }, () => 5);
    }

    [Fact]
    public void UnconfirmedTerminationPreservesTheErrorBeforeAnotherNativeCall()
    {
        var nativeError = 0;
        var error = Assert.Throws<Win32Exception>(() => WindowsChildProcessLifetime.StopAndJoin(
            () => { nativeError = 0; return false; },
            () => { nativeError = 5; return false; },
            _ => { nativeError = 0; return 258; }, () => nativeError));
        Assert.Equal(5, error.NativeErrorCode);
    }

    [Fact]
    public void FailedWaitReportsItsOwnNativeError()
    {
        var nativeError = 0;
        var error = Assert.Throws<Win32Exception>(() => WindowsChildProcessLifetime.StopAndJoin(
            () => false, () => true, _ => { nativeError = 6; return uint.MaxValue; }, () => nativeError));
        Assert.Equal(6, error.NativeErrorCode);
    }

    [Fact]
    public void SuccessfulTerminationRequestDoesNotMakeAnUnconfirmedExitSuccessful()
    {
        Assert.Throws<IOException>(() => WindowsChildProcessLifetime.StopAndJoin(
            () => false, () => true, _ => 258, () => 0));
    }

    [Fact]
    public void JoinFailureStillClosesActualOwnedFileHandles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencam-child-lifetime-{Guid.NewGuid():N}");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var expected = new IOException("unconfirmed child exit");
            var error = Assert.Throws<IOException>(() => WindowsChildProcessLifetime.Dispose(
                () => throw expected, stream));
            Assert.Same(expected, error);
            Assert.False(stream.CanRead);
            // A leaked Windows handle opened without sharing prevents this reopen.
            using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { stream.Dispose(); File.Delete(path); }
    }

    [Fact]
    public void DisposalFailureDoesNotSkipOtherOwnedResourcesOrHideJoinFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencam-child-dispose-{Guid.NewGuid():N}");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var join = new IOException("child exit unconfirmed");
            var close = new IOException("resource close failed");
            var error = Assert.Throws<AggregateException>(() => WindowsChildProcessLifetime.Dispose(
                () => throw join, new FailedClose(close), stream));
            Assert.Equal(new Exception[] { join, close }, error.InnerExceptions);
            Assert.False(stream.CanRead);
        }
        finally { stream.Dispose(); File.Delete(path); }
    }

    private sealed class FailedClose(Exception error) : IDisposable
    {
        public void Dispose() => throw error;
    }
}
