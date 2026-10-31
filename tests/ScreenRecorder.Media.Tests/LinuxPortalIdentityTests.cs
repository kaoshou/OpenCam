// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Platform.Linux;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Interfaces;
namespace ScreenRecorder.Media.Tests;
public sealed class LinuxPortalIdentityTests
{
    [Fact]
    public void SameSizedReplacementCannotRecordUnderAnOlderAcknowledgedIdentity()
    {
        var a="portal:"+new string('a',64);var b="portal:"+new string('b',64);
        var configuration=JsonSerializer.Deserialize<RecordingConfiguration>(JsonSerializer.Serialize(new RecordingConfiguration{ExpectedPortalSourceId=a}))!;
        var selected=new MonitorInfo(0,b,new(0,0,1920,1080),false,1);
        Assert.Throws<ArgumentException>(()=>LinuxRecordingEngine.RequirePortalIdentity(configuration,selected,true));
        configuration.ExpectedPortalSourceId=null;
        Assert.Throws<ArgumentException>(()=>LinuxRecordingEngine.RequirePortalIdentity(configuration,selected,true));
        configuration.ExpectedPortalSourceId=b;
        Assert.Same(selected,LinuxRecordingEngine.RequirePortalIdentity(configuration,selected,true));
        Assert.Same(selected,LinuxRecordingEngine.RequirePortalIdentity(new(),selected,false));
    }
    [Fact]
    public void DefaultPlatformConfigurationDoesNotAddAnUnusedPortalWireField()=>
        Assert.DoesNotContain("ExpectedPortalSourceId",JsonSerializer.Serialize(new RecordingConfiguration()));
}
