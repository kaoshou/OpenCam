// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

public partial class ProjectWorkspaceView
{
    private Guid? _dragClip, _dragProject;
    private long _dragRevision;
    private Point _dragStart, _dragPoint;
    private bool _listDragging;
    private IPointer? _listPointer;
    private ProjectClipDropTarget? _listDrop;
    private DispatcherTimer? _listScroll;

    private void InitializeClipListDrag()
    {
        ClipList.AddHandler(PointerPressedEvent, OnClipDragPressed, RoutingStrategies.Tunnel);
        ClipList.AddHandler(PointerMovedEvent, OnClipDragMoved, RoutingStrategies.Tunnel);
        ClipList.AddHandler(PointerReleasedEvent, OnClipDragReleased, RoutingStrategies.Tunnel);
        ClipList.PointerCaptureLost += (_, _) => CancelClipDrag();
        Closed += (_, _) => CancelClipDrag();
    }

    private void OnClipDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ProjectWorkspaceViewModel vm || !vm.CanEditTimeline || !e.GetCurrentPoint(ClipList).Properties.IsLeftButtonPressed) return;
        var row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (row?.DataContext is not ProjectClip clip) return;
        _dragClip = clip.Id; _dragProject = vm.State.ProjectId; _dragRevision = vm.State.Revision;
        _dragStart = e.GetPosition(ClipList); _listPointer = e.Pointer;
    }

    private void OnClipDragMoved(object? sender, PointerEventArgs e)
    {
        if (_dragClip is null) return;
        _dragPoint = e.GetPosition(ClipList);
        if (!_listDragging && Math.Sqrt(Math.Pow(_dragPoint.X - _dragStart.X, 2) + Math.Pow(_dragPoint.Y - _dragStart.Y, 2)) < 6) return;
        if (!DragStillValid()) { CancelClipDrag(); return; }
        _listDragging = true;
        e.Pointer.Capture(ClipList);
        ClipList.Cursor = new Cursor(StandardCursorType.SizeAll);
        UpdateClipDrop();
        _listScroll ??= new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Input, (_, _) => ScrollClipDrag());
        _listScroll.Start();
        e.Handled = true;
    }

    private bool DragStillValid() => DataContext is ProjectWorkspaceViewModel vm && vm.CanEditTimeline &&
        vm.State.ProjectId == _dragProject && vm.State.Revision == _dragRevision;

    private void UpdateClipDrop()
    {
        _listDrop = null;
        ClipDropMarker.IsVisible = false;
        if (!DragStillValid() || _dragClip is not Guid dragged || _dragPoint.X < 0 || _dragPoint.X > ClipList.Bounds.Width ||
            _dragPoint.Y < 0 || _dragPoint.Y > ClipList.Bounds.Height) return;
        var rows = ClipList.GetRealizedContainers().OfType<ListBoxItem>()
            .Select(row => (Row: row, Top: row.TranslatePoint(default, ClipList)?.Y ?? 0)).OrderBy(x => x.Top).ToArray();
        var hit = rows.FirstOrDefault(x => _dragPoint.Y < x.Top + x.Row.Bounds.Height);
        var target = hit.Row?.DataContext as ProjectClip;
        var after = hit.Row is null || _dragPoint.Y >= hit.Top + hit.Row.Bounds.Height / 2;
        if (!Model.TryResolveDrop(dragged, target?.Id, after, out var drop)) return;
        _listDrop = drop;
        var before = rows.FirstOrDefault(x => (x.Row.DataContext as ProjectClip)?.Id == drop.BeforeClipId);
        var y = before.Row is not null ? before.Top : rows.LastOrDefault() is var last && last.Row is not null
            ? last.Top + last.Row.Bounds.Height : 0;
        ClipDropMarker.Margin = new Thickness(0, Math.Clamp(y, 0, Math.Max(0, ClipList.Bounds.Height - 3)), 0, 0);
        ClipDropMarker.IsVisible = true;
    }

    private void ScrollClipDrag()
    {
        if (!_listDragging || !DragStillValid()) { CancelClipDrag(); return; }
        if (_dragPoint.X < 0 || _dragPoint.X > ClipList.Bounds.Width || _dragPoint.Y < 0 || _dragPoint.Y > ClipList.Bounds.Height)
        { _listScroll?.Stop(); ClipDropMarker.IsVisible = false; return; }
        var delta = _dragPoint.Y < 24 ? -12 : _dragPoint.Y > ClipList.Bounds.Height - 24 ? 12 : 0;
        var scroll = ClipList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is not null && delta != 0)
        {
            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + delta, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
            UpdateClipDrop();
        }
    }

    private async void OnClipDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_listDragging) { CancelClipDrag(); return; }
        _dragPoint = e.GetPosition(ClipList); UpdateClipDrop();
        var drop = DragStillValid() ? _listDrop : null;
        CancelClipDrag(); e.Handled = true;
        if (drop is not null) await Model.MoveClipAsync(drop.ClipId, drop.BeforeClipId);
    }

    private void CancelClipDrag()
    {
        _listScroll?.Stop(); _listDragging = false; _dragClip = null; _listDrop = null;
        ClipDropMarker.IsVisible = false; ClipList.Cursor = null;
        var pointer = _listPointer; _listPointer = null;
        if (pointer?.Captured == ClipList) pointer.Capture(null);
    }
}
