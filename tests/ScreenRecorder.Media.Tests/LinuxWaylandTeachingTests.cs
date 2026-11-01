// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Platform.Linux;

namespace ScreenRecorder.Media.Tests;

public sealed class LinuxWaylandTeachingTests
{
    private static readonly Guid Instance=Guid.Parse("d77d098c-856f-4bf1-b27b-590b6b204bbb");
    private const string Capabilities="{\"supported\":true,\"gnomeVersion\":\"46.0\",\"desktop\":\"wayland\",\"authorized\":false}";
    private static string Reply(string data=Capabilities,int protocol=1,string status="ok",Guid? instance=null)=>
        $"{{\"protocol\":{protocol},\"instance\":\"{instance??Instance:D}\",\"status\":\"{status}\",\"data\":{data}}}";

    [Fact]
    public void SupportedCapabilitiesRequireExactProtocolDesktopAndGnomeFamily()
    {
        var reply=LinuxTeachingReply.Parse(Reply());
        Assert.Equal(Instance,reply.Instance);
        var caps=reply.ReadCapabilities();
        Assert.True(caps.Supported);Assert.False(caps.Authorized);
        foreach(var data in new[]{Capabilities.Replace("46.0","47.0"),Capabilities.Replace("wayland","x11"),Capabilities.Replace("true","false")})
            Assert.False(LinuxTeachingReply.Parse(Reply(data)).ReadCapabilities().Supported);
    }

    [Fact]
    public void ExtensionRestartCannotReuseAnEarlierInstance()
    {
        Assert.Equal(Instance,LinuxTeachingReply.Parse(Reply(),Instance).Instance);
        Assert.Throws<IOException>(()=>LinuxTeachingReply.Parse(Reply(instance:Guid.NewGuid()),Instance));
    }

    [Fact]
    public void InvalidPrivateReplyDoesNotLeakPayloadOrRetainJsonException()
    {
        const string secret="do-not-copy-private-payload";
        foreach(var text in new[]{"not json "+secret,Reply(protocol:2),Reply(status:secret),Reply().Replace("\"data\":","\"unknown\":true,\"data\":"),
            Reply().Replace(Instance.ToString(),secret),new string('x',65537),"\""+new string('界',24000)+"\""})
        {
            var error=Assert.Throws<IOException>(()=>LinuxTeachingReply.Parse(text));
            Assert.DoesNotContain(secret,error.Message);Assert.Null(error.InnerException);
        }
    }

    [Fact]
    public void CapabilityFieldsCannotSmuggleUnknownDataOrWrongTypes()
    {
        foreach(var data in new[]{Capabilities.Replace("false","\"false\""),Capabilities.Replace("true","1"),
            Capabilities.Replace("}",",\"restoreToken\":\"private\"}"),"[]","null"})
            Assert.Throws<IOException>(()=>LinuxTeachingReply.Parse(Reply(data)).ReadCapabilities());
    }

    [Fact]
    public void DeniedOrUnavailableCapabilityIsNotPermissionToEnableTools()
    {
        foreach(var status in new[]{"denied","unavailable","busy"})
        {
            var caps=LinuxTeachingReply.Parse(Reply(status:status)).ReadCapabilities();
            Assert.False(caps.Supported);Assert.False(caps.Authorized);
        }
    }
}
