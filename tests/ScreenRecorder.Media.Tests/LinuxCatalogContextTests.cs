// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
namespace ScreenRecorder.Media.Tests;
public sealed class LinuxCatalogContextTests
{
    [Fact]
    public void FailedQueryReportsExitAndSafeCodeWithoutRawDeviceDetails()
    {
        if (OperatingSystem.IsWindows()) return;
        var error = Assert.Throws<IOException>(() => LinuxDesktopCatalog.Run("/bin/sh", "-c", "printf '%s\\n' 'OCLINUX {\"event\":\"error\",\"code\":\"graphics_startup\",\"device\":\"private-device\"}' >&2; exit 42"));
        Assert.Contains("exit=42", error.Message); Assert.Contains("graphics_startup", error.Message); Assert.DoesNotContain("private-device", error.Message);
    }
    [Fact]
    public void SynchronousCatalogQueryDoesNotWaitForBlockedUiContext()
    {
        if (OperatingSystem.IsWindows()) return;
        var context = new PausedContext(); string? output = null; Exception? failure = null;
        var worker = new Thread(() => {
            SynchronizationContext.SetSynchronizationContext(context);
            try { output = LinuxDesktopCatalog.Run("/bin/sh", "-c", "sleep 0.1; printf ready"); }
            catch (Exception e) { failure = e; }
        }) { IsBackground = true };
        worker.Start(); var completed = worker.Join(TimeSpan.FromSeconds(3));
        context.Release(); Assert.True(worker.Join(TimeSpan.FromSeconds(3)));
        Assert.True(completed, "Catalog pipe reads captured the blocked UI synchronization context.");
        Assert.Null(failure); Assert.Equal("ready", output);
    }
    private sealed class PausedContext : SynchronizationContext {
        private readonly object gate = new(); private bool released;
        private readonly List<(SendOrPostCallback callback, object? state)> pending = [];
        public override void Post(SendOrPostCallback d, object? state) {
            lock (gate) { if (!released) { pending.Add((d,state)); return; } }
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
        public void Release() {
            lock (gate) { released = true; foreach (var item in pending) ThreadPool.QueueUserWorkItem(_ => item.callback(item.state)); pending.Clear(); }
        }
    }
}
