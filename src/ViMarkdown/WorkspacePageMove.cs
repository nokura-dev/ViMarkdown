using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private static readonly DataFormat<string> PageDragFormat = DataFormat.CreateStringApplicationFormat("ViMarkdown.Page");
    private readonly string _pageDragToken = Guid.NewGuid().ToString();
    private long[]? _draggedPageIds;

    private void ConfigurePageDragDrop()
    {
        Point start = default;
        long? pressedPage = null;
        bool collapseOnRelease = false;
        ListBoxItem? hoveredNote = null;
        void SetHoveredNote(ListBoxItem? row)
        {
            if (ReferenceEquals(hoveredNote, row)) return;
            hoveredNote?.Classes.Remove("page-drop-hover");
            hoveredNote = row;
            hoveredNote?.Classes.Add("page-drop-hover");
        }
        _pages.AddHandler(PointerPressedEvent, (_, e) =>
        {
            pressedPage = null;
            if (!e.GetCurrentPoint(_pages).Properties.IsLeftButtonPressed) return;
            var row = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (row?.DataContext is not WorkspaceItem { IsNote: false } page) return;
            start = e.GetPosition(_pages);
            pressedPage = page.Id;
            collapseOnRelease = false;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _pages.Items.Cast<WorkspaceItem>().Any(p => p.Id == _activePageId))
                SelectPageRange(_pages.Items.IndexOf(page));
            else if (SelectedPageIds().Contains(page.Id))
                collapseOnRelease = true;
            else SelectSinglePage(page);
            _pages.Focus();
            e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        _pages.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (pressedPage is not { } id) return;
            pressedPage = null;
            if (collapseOnRelease && _pages.Items.Cast<WorkspaceItem>().FirstOrDefault(p => p.Id == id) is { } page)
                SelectSinglePage(page);
            _pages.Focus();
            e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        _pages.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (!e.GetCurrentPoint(_pages).Properties.IsLeftButtonPressed) { pressedPage = null; return; }
            var delta = e.GetPosition(_pages) - start;
            if (pressedPage is null || Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)) < 6) return;
            pressedPage = null;
            _draggedPageIds = SelectedPageIds();
            try
            {
                using var data = new DataTransfer();
                data.Add(DataTransferItem.Create(PageDragFormat, _pageDragToken));
                await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
            }
            catch (Exception ex) { _saveStatus.Text = "操作失敗: " + ex.Message; }
            finally { _draggedPageIds = null; SetHoveredNote(null); }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        DragDrop.SetAllowDrop(_notes, true);
        NoteChoice? Target(DragEventArgs e)
        {
            if (_draggedPageIds is null || e.DataTransfer.TryGetValue(PageDragFormat) != _pageDragToken) return null;
            var row = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            return row is not null && _notes.IndexFromContainer(row) >= 0 ? row.DataContext as NoteChoice : null;
        }
        void UpdateDragHover(object? sender, DragEventArgs e)
        {
            e.DragEffects = Target(e) is { } target &&
                _store.Items().Any(p => _draggedPageIds!.Contains(p.Id) && !p.IsNote && p.NoteId != target.Id)
                ? DragDropEffects.Move : DragDropEffects.None;
            SetHoveredNote(e.DragEffects == DragDropEffects.Move
                ? (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()
                : null);
            e.Handled = true;
        }
        DragDrop.AddDragEnterHandler(_notes, UpdateDragHover);
        DragDrop.AddDragOverHandler(_notes, UpdateDragHover);
        DragDrop.AddDragLeaveHandler(_notes, (_, _) => SetHoveredNote(null));
        DragDrop.AddDropHandler(_notes, (_, e) =>
        {
            SetHoveredNote(null);
            e.DragEffects = DragDropEffects.None;
            if (Target(e) is { } target && _draggedPageIds is { } ids)
            {
                try { if (MovePagesToNote(ids, target.Id)) e.DragEffects = DragDropEffects.Move; }
                catch (Exception ex) { _saveStatus.Text = "操作失敗: " + ex.Message; }
            }
            e.Handled = true;
        });
    }

    private bool MovePageToNote(long id, long? noteId)
        => MovePagesToNote([id], noteId);

    private bool MovePagesToNote(long[] ids, long? noteId)
    {
        var pages = _store.Items().Where(p => ids.Contains(p.Id) && !p.IsNote && p.NoteId != noteId).ToList();
        if (pages.Count == 0 || !SaveCurrent()) return false;
        _store.MovePages(pages.Select(p => p.Id), noteId);
        if (pages.Any(p => p.Id == _activePageId)) _noteId = noteId;
        RefreshDetachedPageMetadata();
        return true;
    }

}
