// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit.Sdk;

[assembly: ScreenRecorder.Media.Tests.HeadlessDispatcherCleanup]

namespace ScreenRecorder.Media.Tests;

// Test-only lifecycle hook. Never substitute a font service or suppress a callback failure.
public sealed class HeadlessDispatcherCleanupAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest)
    {
        // Avalonia 11.2.5 disposes its service scope before ResetForUnitTests
        // processes remaining jobs. Finish them while font/layout services exist.
        if (Application.Current is ProjectLayoutApp && Dispatcher.UIThread.CheckAccess())
            Dispatcher.UIThread.RunJobs();
    }
}

public sealed class HeadlessDispatcherCleanupTests
{
    [Fact]
    public async Task PendingTextLayoutFinishesBeforeHeadlessServicesAreDisposed()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(ProjectLayoutApp));
        var measured = 0;
        await session.Dispatch(() =>
        {
            // Two queued layouts expose teardown failure both at the first scope's
            // disposal and at the next scope's initial dispatcher reset.
            for (var i = 0; i < 2; i++)
                Dispatcher.UIThread.Post(() =>
                {
                    var text = new TextBlock { Text = "GIF 匯出" };
                    text.Measure(new Size(300, 100));
                    Assert.True(text.DesiredSize.Width > 0);
                    measured++;
                }, DispatcherPriority.Render);
            new HeadlessDispatcherCleanupAttribute().After(GetType().GetMethod(nameof(PendingTextLayoutFinishesBeforeHeadlessServicesAreDisposed))!);
        }, CancellationToken.None);
        await session.Dispatch(() => Assert.Equal(2, measured), CancellationToken.None);
    }
}
