// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class EditorRequestMetricsTests
{
    [Fact]
    public void MetricsAggregateOnlyKnownOperationsWithoutPathsOrUnboundedSamples()
    {
        var metrics = new EditorRequestMetrics();
        metrics.Record("GetProjectFrame", 10); metrics.Record("GetProjectFrame", 30);
        metrics.Record("/private/secret.mp4", 100);
        metrics.Record("GetProjectWaveform", double.NaN);
        var text = metrics.Describe();
        Assert.Contains("GetProjectFrame: count=2, mean=20.0 ms, max=30.0 ms", text);
        Assert.DoesNotContain("secret", text); Assert.DoesNotContain("NaN", text);
        Assert.DoesNotContain("GetProjectWaveform", text);
    }
}
