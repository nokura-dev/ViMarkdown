using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private WorkspaceStore _store = null!;
    private readonly ListBox _notes = new();
    private readonly ListBox _pages = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TextBlock _saveStatus = new() { Foreground = WorkspaceColors.Accent };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private PageSortOrder _pageSortOrder;
    private long? _noteId, _activePageId;
    private long? _automaticPageNameId;
    private bool _loading, _dirty, _rebuilding;
    private sealed record NoteChoice(long? Id, string Label) { public override string ToString() => Label; }

    private Control BuildWorkspace(Control editor, string? databasePath)
    {
        _store = _workspaceOwner?._store ?? new WorkspaceStore(databasePath ?? Environment.GetEnvironmentVariable("VIMARKDOWN_DB") ??
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ViMarkdown", "workspace.db"));
        ConfigureWindowPlacement();
        if (Enum.TryParse<PageSortOrder>(_store.GetSetting("page-sort"), out var order) && Enum.IsDefined(order)) _pageSortOrder = order;
        _rich.ImageResolver = _store.GetImage;
        _rich.ImageCopyRequested += async data => await CopyImageAsync(data);
        _saveTimer.Tick += (_, _) => SaveCurrent();
        Closing += (_, e) => { if (!PrepareEditorWindowsForClose()) e.Cancel = true; };
        Closed += (_, _) =>
        {
            _saveTimer.Stop();
            ++_modeSwitchGeneration;
            _rich.ImageResolver = null;
            _rich.ClearPage();
            try { SaveWindowPlacement(); }
            catch (Exception) { /* Closing must release the page even if placement cannot be saved. */ }
            finally
            {
                if (!IsDetachedEditor) _store.Dispose();
                DetachedEditorClosed();
            }
        };
        if (IsDetachedEditor) return BuildDetachedEditorLayout(editor);
        var grid = BuildWorkspaceLayout(WrapEditorForDetachment(editor));
        _notes.SelectionChanged += (_, _) =>
        {
            if (_rebuilding || _notes.SelectedItem is not NoteChoice choice) return;
            if (!SaveCurrent()) { ReloadLists(); return; }
            OpenPage(null);
            _noteId = choice.Id; _store.SetSetting("note", _noteId?.ToString()); ReloadLists(openTimelinePage: false);
        };
        _pages.SelectionChanged += (_, _) =>
        {
            if (_rebuilding || _selectingPageRange) return;
            OpenPage((_pages.SelectedItem as WorkspaceItem)?.Id);
            _pageRangeEndId = _activePageId;
            if (_pageSortOrder != PageSortOrder.Name) ReloadLists();
        };
        ConfigurePageDragDrop();
        if (_store.GetSetting("initialized") is null)
        {
            var readme = ReadInitialPage("README.md");
            var sample = ReadInitialPage("MarkdownSample.md");
            var id = _store.Create("README.md", false, null, readme);
            _store.Create("Markdown記法サンプル", false, null, sample);
            _store.SetSetting("active", id.ToString()); _store.SetSetting("initialized", "1");
        }
        var items = _store.Items();
        if (long.TryParse(_store.GetSetting("note"), out var note) && items.Any(i => i.Id == note && i.IsNote)) _noteId = note;
        if (long.TryParse(_store.GetSetting("active"), out var active) && items.FirstOrDefault(i => i.Id == active && !i.IsNote) is { } item)
        { _noteId = item.NoteId; OpenPage(active); }
        else OpenPage(null);
        ReloadLists(); return grid;
    }
    private static string ReadInitialPage(string name)
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("ViMarkdown.InitialPages." + name)
            ?? throw new InvalidOperationException("初期ページが見つかりません: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private List<NoteChoice> NoteChoices(IEnumerable<WorkspaceItem>? items = null)
    {
        var result = new List<NoteChoice> { new(null, "未分類") };
        result.AddRange(WorkspaceOrdering.ByName((items ?? _store.Items()).Where(i => i.IsNote)).Select(i => new NoteChoice(i.Id, i.Name)));
        return result;
    }
    private void ReloadLists(bool openTimelinePage = true)
    {
        if (IsDetachedEditor) return;
        var focusedList = _notes.IsKeyboardFocusWithin ? _notes : _pages.IsKeyboardFocusWithin ? _pages : null;
        var selected = SelectedPageIds();
        _rebuilding = true;
        try
        {
            var items = _store.Items();
            var matching = items.Where(i => !i.IsNote && TimelinePeriods.Matches(i.UpdatedAt, _timelinePeriod, DateTime.Today)).ToList();
            var choices = _timelinePeriod == 0 ? NoteChoices(items) : NoteChoices(items).Where(n => matching.Any(p => p.NoteId == n.Id)).ToList();
            if (!choices.Any(n => n.Id == _noteId)) _noteId = choices.FirstOrDefault()?.Id;
            _notes.ItemsSource = choices; _notes.SelectedItem = choices.FirstOrDefault(i => i.Id == _noteId);
            var pages = WorkspaceOrdering.Pages(matching.Where(i => i.NoteId == _noteId), _pageSortOrder).ToList();
            _pages.ItemsSource = pages; _pages.SelectedItem = pages.FirstOrDefault(i => i.Id == _activePageId);
            if (selected.Contains(_activePageId ?? -1))
                for (var i = 0; i < pages.Count; i++)
                    if (selected.Contains(pages[i].Id)) _pages.Selection.Select(i);
            if (!pages.Any(p => p.Id == _pageRangeEndId)) _pageRangeEndId = _activePageId;
            _store.SetSetting("note", _noteId?.ToString());
            if (openTimelinePage && _timelinePeriod != 0 && !pages.Any(p => p.Id == _activePageId)) OpenPage(pages.FirstOrDefault()?.Id);
        }
        finally
        {
            _rebuilding = false;
            if (IsActive)
            {
                if (_activePageId is null) _notes.Focus();
                else focusedList?.Focus();
            }
        }
    }
    private void OpenPage(long? id)
    {
        if (!SaveCurrent()) return;
        _automaticPageNameId = null;
        ClearWorkspaceHighlights();
        CancelPendingCommand(); _commandPrompt = null; _lastVisual = null; _visualKind = 0; _replaceMode = false; _continueInsertUndo = false; _blockInsertStart = null; _blockInsertTargets = null; _ime.Cancel(); _loading = true;
        try
        {
            var item = _store.Items().FirstOrDefault(i => i.Id == id && !i.IsNote);
            _rich.ClearPage();
            foreach (var markdown in _editor.TextArea.TextView.LineTransformers.OfType<LightPlusMarkdown>())
                markdown.ClearCache();
            _activePageId = item?.Id; _pageRangeEndId = _activePageId; _pageContent.Text = IsPageDetached ? "" : item?.Body ?? ""; _pageContent.UndoStack.ClearAll();
            _caret = 0; _anchor = null; _editor.CaretOffset = 0; _modeSwitchGeneration++;
            _preview.Offset = default; _editor.ScrollToHome();
            _editor.IsEnabled = _rich.IsEnabled = item is not null && !IsPageDetached;
            _paneBodies[3].IsEnabled = item is not null;
            _editor.IsVisible = item is not null && _mode == EditorMode.Insert;
            _preview.IsVisible = item is not null && _mode != EditorMode.Insert;
            Title = item is null ? "ViMarkdown" : FormatPageWindowTitle(item.Name);
            if (!IsDetachedEditor) _store.SetSetting("active", _activePageId?.ToString());
            UpdateDetachedPageNotice();
            _saveStatus.Text = item is null ? "ページを選択するか、作成してください" : "保存済み";
            Refresh();
        }
        finally { _loading = false; }
        if (_activePageId is null) _notes.Focus();
    }
    private void ScheduleSave()
    {
        if (_loading || _activePageId is null || IsPageDetached) return;
        _dirty = true; _saveStatus.Text = "未保存…"; _saveTimer.Stop(); _saveTimer.Start();
    }
    private bool UpdateAutomaticPageName(long id)
    {
        if (_automaticPageNameId != id) return false;
        var page = _store.Items().FirstOrDefault(p => p.Id == id);
        if (page is null) return false;
        var name = PageNaming.AutomaticName(_pageContent.Text);
        if (string.IsNullOrWhiteSpace(name) || name == page.Name) return false;
        _store.Rename(id, name);
        Title = FormatPageWindowTitle(name);
        return true;
    }

    private static string FormatPageWindowTitle(string name) =>
        PageNaming.WindowTitle(name);

    private bool SaveCurrent()
    {
        _saveTimer.Stop();
        if (!_dirty || _activePageId is null || IsPageDetached) return true;
        try
        {
            _store.Save(_activePageId.Value, _pageContent.Text);
            var renamed = UpdateAutomaticPageName(_activePageId.Value);
            _dirty = false; _saveStatus.Text = "保存済み";
            if (IsDetachedEditor) _workspaceOwner!.RefreshDetachedPageMetadata();
            if ((renamed || _pageSortOrder != PageSortOrder.Name || _timelinePeriod != 0) && !_rebuilding) ReloadLists();
            else
            {
                var page = _store.Items().FirstOrDefault(i => i.Id == _activePageId);
                if (page is not null)
                    foreach (var date in _pages.GetVisualDescendants().OfType<TextBlock>()
                        .Where(t => t.Name == "PageUpdatedAt" && t.Tag is long id && id == page.Id))
                        date.Text = page.UpdatedAt.ToLocalTime().ToString(PageNaming.TimestampFormat);
            }
            return true;
        }
        catch (Exception ex) { _saveStatus.Text = "保存失敗（内容を保持しています）: " + ex.Message; return false; }
    }
}
