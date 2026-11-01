// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenRecorder.Platform.Linux;

internal sealed record LinuxTeachingCapabilities(bool Supported,bool Authorized);

// Private extension channel data is never a UI/project DTO. In particular,
// JsonException input excerpts and arbitrary server errors must not escape.
internal sealed class LinuxTeachingReply
{
    private readonly JsonElement data;
    internal Guid Instance { get; }
    internal string Status { get; }
    private LinuxTeachingReply(Guid instance,string status,JsonElement value)=>(Instance,Status,data)=(instance,status,value);
    internal static LinuxTeachingReply Parse(string text,Guid? expectedInstance=null)
    {
        try {
            if(text is null||Encoding.UTF8.GetByteCount(text)>65536)throw new InvalidDataException();
            using var doc=JsonDocument.Parse(text,new JsonDocumentOptions {MaxDepth=16});
            var root=doc.RootElement;
            RequireKeys(root,"protocol","instance","status","data");
            if(!root.GetProperty("protocol").TryGetInt32(out var protocol)||protocol!=1||
                !Guid.TryParseExact(root.GetProperty("instance").GetString(),"D",out var instance)||instance==Guid.Empty||
                expectedInstance is {} expected&&expected!=instance)throw new InvalidDataException();
            var status=root.GetProperty("status").GetString();
            if(status is not("ok" or "denied" or "unavailable" or "busy"))throw new InvalidDataException();
            return new(instance,status,root.GetProperty("data").Clone());
        }catch(Exception e) when(e is JsonException or InvalidDataException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException) {
            throw new IOException("The Wayland teaching integration response was unavailable or invalid.");
        }
    }
    internal LinuxTeachingCapabilities ReadCapabilities()
    {
        if(Status!="ok")return new(false,false);
        try {
            RequireKeys(data,"supported","gnomeVersion","desktop","authorized");
            var supported=data.GetProperty("supported").GetBoolean();
            var authorized=data.GetProperty("authorized").GetBoolean();
            var version=data.GetProperty("gnomeVersion").GetString();
            var desktop=data.GetProperty("desktop").GetString();
            if(version is null||version.Length>16||desktop is null||desktop.Length>32)throw new InvalidDataException();
            supported=supported&&Regex.IsMatch(version,@"\A46(?:\.\d{1,3}){0,3}\z")&&desktop=="wayland";
            return new(supported,supported&&authorized);
        }catch(Exception e) when(e is JsonException or InvalidDataException or InvalidOperationException or FormatException or KeyNotFoundException) {
            throw new IOException("The Wayland teaching integration capability was unavailable or invalid.");
        }
    }
    internal static void RequireKeys(JsonElement value,params string[] expected)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new InvalidDataException();
        var remaining=new HashSet<string>(expected,StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())if(!remaining.Remove(property.Name))throw new InvalidDataException();
        if(remaining.Count!=0)throw new InvalidDataException();
    }
    public override string ToString()=>"Wayland teaching integration response (redacted)";
}
