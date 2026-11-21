using Avalonia.Controls;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private bool _selectingPageRange;
    private long? _pageRangeEndId;

    private long[] SelectedPageIds() => _pages.SelectedItems!.OfType<WorkspaceItem>().Select(p => p.Id).ToArray();

    private void SelectSinglePage(WorkspaceItem page)
    {
        _selectingPageRange = true;
        try
        {
            _pages.Selection.Clear();
            if (page.Id == _activePageId)
            {
                _pages.SelectedItem = page;
                _pageRangeEndId = page.Id;
                return;
            }
        }
        finally { _selectingPageRange = false; }
        _pages.SelectedItem = page;
    }

    private void SelectPageRange(int end)
    {
        var pages = _pages.Items.Cast<WorkspaceItem>().ToList();
        var anchor = pages.FindIndex(p => p.Id == _activePageId);
        if (anchor < 0 || end < 0 || end >= pages.Count) return;
        _selectingPageRange = true;
        try
        {
            _pages.Selection.Clear();
            _pages.Selection.SelectRange(Math.Min(anchor, end), Math.Max(anchor, end));
            _pageRangeEndId = pages[end].Id;
        }
        finally { _selectingPageRange = false; }
    }

    private void NavigatePages(int delta, bool extend)
    {
        if (_pages.ItemCount == 0) return;
        var pages = _pages.Items.Cast<WorkspaceItem>().ToList();
        var current = pages.FindIndex(p => p.Id == (_pageRangeEndId ?? _activePageId));
        var next = current < 0 ? 0 : Math.Clamp(current + delta, 0, pages.Count - 1);
        if (extend && pages.Any(p => p.Id == _activePageId)) SelectPageRange(next);
        else SelectSinglePage(pages[next]);
        _pages.ScrollIntoView(next);
        _pages.Focus();
    }
}
