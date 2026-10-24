// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

/// <summary>One verified streaming cursor and one frame; common to preview and exports.
/// Callers serialize frames and retain the original immutable timing plan.</summary>
public sealed class ProjectPointerCompositor(ProjectRenderPlan plan,
    Func<ProjectSource,CancellationToken,Task<PointerTrackCursor>> open) : IDisposable
{
    private PointerTrackCursor? _cursor;
    private Guid _sourceId;
    private bool _disposed;
    private (Guid Clip,int Width,int Height,bool Preview,bool Gif)? _geometryKey;
    private ProjectPointerGeometry? _geometry;
    private readonly HashSet<Guid> _unavailable = [];
    public bool HadUnavailablePointer { get; private set; }

    public static bool IsRequired(ProjectRenderPlan plan) =>
        plan.PointerSettings.Style != ProjectPointerStyle.Hidden && plan.Clips.Any(c=>c.Source.Pointer!=null);

    public async ValueTask ApplyAsync(byte[] rgba,long absoluteFrame,int width,int height,
        bool preview=false,bool gif=false,CancellationToken ct=default)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        ct.ThrowIfCancellationRequested();
        if (!IsRequired(plan)) return;
        var position=plan.LocateVideoFrame(absoluteFrame);
        if(position is null) return;
        var part=plan.Clips.First(c=>c.Clip.Id==position.ClipId);
        if(part.Source.Pointer is null || _unavailable.Contains(part.Source.Id)) return;
        try
        {
        if(_cursor is null || _sourceId!=part.Source.Id)
        {
            _cursor?.Dispose(); _cursor=null;
            _cursor=await open(part.Source,ct).ConfigureAwait(false);
            _sourceId=part.Source.Id;
        }
        var key=(part.Clip.Id,width,height,preview,gif);
        if(_geometryKey!=key)
        {
            _geometry=ProjectPointerGeometry.Create(part.Source,part.Clip,plan.Canvas,width,height,preview,gif);
            _geometryKey=key;
        }
        var point=await _cursor.ResolveAsync(position.SourcePts,ct).ConfigureAwait(false);
        PointerRasterizer.Composite(rgba,width,height,_geometry!.Map(point),plan.PointerSettings,_geometry.RasterScale);
        }
        catch (Exception ex) when (preview && ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _cursor?.Dispose(); _cursor = null;
            _unavailable.Add(part.Source.Id);
            HadUnavailablePointer = true;
            Serilog.Log.Warning("Pointer preview data unavailable; original video and audio remain available.");
        }
    }

    public void Dispose() { _disposed=true;_cursor?.Dispose();_cursor=null; }
}
