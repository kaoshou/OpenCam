using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public class MacOsPermissionTests
{
    [Fact]
    public void HasPermission_DelegatesToPreflight()
    {
        var api = new FakePermissionApi { PreflightResult = true };
        var service = new MacOsScreenCapturePermissionService(api);

        Assert.True(service.HasPermission());
        Assert.Equal(1, api.PreflightCalls);
    }

    [Fact]
    public void RequestPermission_DelegatesToRequest()
    {
        var api = new FakePermissionApi { RequestResult = true };
        var service = new MacOsScreenCapturePermissionService(api);

        Assert.True(service.RequestPermission());
        Assert.Equal(1, api.RequestCalls);
    }

    private sealed class FakePermissionApi : IMacOsScreenCapturePermissionApi
    {
        public bool PreflightResult { get; init; }
        public bool RequestResult { get; init; }
        public int PreflightCalls { get; private set; }
        public int RequestCalls { get; private set; }

        public bool Preflight()
        {
            PreflightCalls++;
            return PreflightResult;
        }

        public bool Request()
        {
            RequestCalls++;
            return RequestResult;
        }
    }
}
