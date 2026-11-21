using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private void RunWorkspaceSearch()
    {
        var keyword = _searchKeyword.Text ?? "";
        if (string.IsNullOrWhiteSpace(keyword)) { _searchKeyword.Focus(); return; }
        var scope = (WorkspaceSearchScope)_searchScope.SelectedIndex;
        var hits = WorkspaceSearch.Find(_store.Items(), scope, _noteId, _activePageId, keyword, _pageContent.Text);
        if (scope == WorkspaceSearchScope.CurrentPage)
        {
            _searchResultsPane.IsVisible = false;
            if (hits.Count == 0) { ClearWorkspaceHighlights(); _saveStatus.Text = "一致するキーワードがありません。"; return; }
            var matches = WorkspaceSearch.Matches(_pageContent.Text, keyword);
            var caret = _mode == EditorMode.Insert ? _editor.CaretOffset : _caret;
            var repeat = _lastPageSearch == keyword && _lastSearchPage == _activePageId;
            var next = matches.FirstOrDefault(m => repeat ? m.Start > caret : m.Start >= caret, matches[0]);
            _lastPageSearch = keyword; _lastSearchPage = _activePageId;
            JumpToSearchOffset(next.Start, keyword);
            return;
        }
        _lastPageSearch = "";
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

    private void NavigateToSearchHit(WorkspaceSearchHit hit)
    {
        if (!SaveCurrent()) return;
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
        JumpToSearchOffset(match.Offset, hit.Keyword);
    }

    private void JumpToSearchOffset(int offset, string keyword)
    {
        CancelPendingCommand(); _commandPrompt = null; _promptOpeningText = null;
        if (_mode == EditorMode.Visual) SwitchMode(EditorMode.Normal);
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
        FocusEditingSurface();
        var page = _activePageId;
        Dispatcher.UIThread.Post(() =>
        {
            if (_activePageId != page || _highlightKeyword != keyword) return;
            UpdateLayout();
            if (_mode == EditorMode.Insert) _editor.TextArea.Caret.BringCaretToView();
            else _rich.BringCaretIntoView();
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
