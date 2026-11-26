using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private int _searchNavigationGeneration;
    private void RunWorkspaceSearch()
    {
        var keyword = _searchKeyword.Text ?? "";
        if (string.IsNullOrWhiteSpace(keyword)) { _searchKeyword.Focus(); return; }
        if (!SaveAllEditors()) return;
        var scope = IsDetachedEditor ? WorkspaceSearchScope.CurrentPage : (WorkspaceSearchScope)_searchScope.SelectedIndex;
        var hits = WorkspaceSearch.Find(_store.Items(), scope, _noteId, _activePageId, keyword, IsPageDetached ? null : _pageContent.Text);
        if (hits.Count == 0) ClearWorkspaceHighlights();
        _searchResults.ItemsSource = hits;
        _searchResultsPane.IsVisible = true;
        _searchSummary.Text = hits.Count == 0 ? "一致する結果がありません。" : $"「{keyword}」 — {hits.Count}行";
        CancelPendingCommand();
        _commandPrompt = null;
        _promptOpeningText = null;
        _searchResults.SelectedIndex = hits.Count > 0 ? 0 : -1;
        UpdateStatus();
        UpdateLayout();
        _searchResults.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToHome();
        FocusPaneList(_searchResults);
    }

    private void NavigateToSearchHit(WorkspaceSearchHit hit, bool keepResultsFocus)
    {
        if (!SaveAllEditors()) return;
        var items = _store.Items();
        var page = items.FirstOrDefault(p => !p.IsNote && p.Id == hit.PageId);
        if (page is null) { _saveStatus.Text = "検索結果のページは削除されています。再検索してください。"; return; }
        var current = WorkspaceSearch.Find(items, WorkspaceSearchScope.CurrentPage, page.NoteId, page.Id, hit.Keyword);
        var match = current.OrderBy(h => Math.Abs((long)h.LineNumber - hit.LineNumber)).FirstOrDefault();
        if (match is null) { _saveStatus.Text = "一致箇所が変更されています。再検索してください。"; return; }
        _timelinePeriod = 0; _updateTimelineSelection?.Invoke();
        _noteId = page.NoteId;
        if (_activePageId != page.Id) OpenPage(page.Id);
        ReloadLists();
        if (_detachedEditors.TryGetValue(page.Id, out var window))
        {
            BringEditorToFront(window);
            window.JumpToSearchOffset(match.Offset, hit.Keyword, keepResultsFocus: false);
            return;
        }
        JumpToSearchOffset(match.Offset, hit.Keyword, keepResultsFocus);
    }

    private void JumpToSearchOffset(int offset, string keyword, bool keepResultsFocus)
    {
        CancelPendingCommand(); _commandPrompt = null; _promptOpeningText = null;
        if (_mode == EditorMode.Visual) SwitchMode(EditorMode.Normal, focusEditor: !keepResultsFocus);
        _anchor = null;
        _caret = offset;
        _editor.Select(offset, 0);
        _highlightKeyword = keyword;
        _workspaceSearchRanges = WorkspaceSearch.Matches(_pageContent.Text, keyword);
        _rich.SearchRanges = _workspaceSearchRanges;
        Refresh();
        // Keep the exact source offset even when matching Markdown delimiters.
        _caret = offset; _rich.Caret = offset;
        _rich.InvalidateVisual(); _editor.TextArea.TextView.Redraw();
        if (keepResultsFocus) _searchResults.Focus();
        else FocusEditingSurface();
        var page = _activePageId;
        var navigation = ++_searchNavigationGeneration;
        var modeSwitch = _modeSwitchGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (navigation != _searchNavigationGeneration || _activePageId != page ||
                _highlightKeyword != keyword || _modeSwitchGeneration != modeSwitch) return;
            SyncRichContentPadding();
            UpdateLayout();
            // Bringing the whole rich surface into view can clamp the request to
            // its old viewport. Use document coordinates after layout instead.
            // A scroll can also change scrollbar width and wrapping, so settle
            // the layout before computing the final position.
            for (var pass = 0; pass < 4; pass++)
            {
                if (_mode == EditorMode.Insert)
                {
                    _editor.TextArea.Caret.BringCaretToView();
                    UpdateLayout();
                    var view = _editor.TextArea.TextView;
                    var scroll = view.GetVisualAncestors().OfType<ScrollViewer>().First();
                    var caret = view.GetVisualPosition(_editor.TextArea.Caret.Position,
                        AvaloniaEdit.Rendering.VisualYPosition.LineMiddle);
                    scroll.Offset = new Avalonia.Vector(scroll.Offset.X,
                        Math.Clamp(caret.Y - scroll.Viewport.Height / 2, 0,
                            Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
                }
                else
                {
                    var caret = _rich.CaretBounds;
                    _preview.Offset = new Avalonia.Vector(_preview.Offset.X,
                        Math.Clamp(caret.Center.Y - _preview.Viewport.Height / 2, 0,
                            Math.Max(0, _preview.Extent.Height - _preview.Viewport.Height)));
                }
                UpdateLayout();
            }
        }, DispatcherPriority.Loaded);
    }

    private void ClearWorkspaceHighlights()
    {
        _highlightKeyword = "";
        _workspaceSearchRanges = [];
        _rich.SearchRanges = [];
        _rich.InvalidateVisual();
        _editor.TextArea.TextView.Redraw();
    }
}
