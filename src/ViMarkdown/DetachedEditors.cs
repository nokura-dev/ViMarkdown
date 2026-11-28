using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    // All editor windows belong to this workspace in the same process. Only the
    // registered window may edit its page; the workspace keeps its selection.
    private readonly MainWindow? _workspaceOwner;
    private readonly long? _detachedPageId;
    private readonly Dictionary<long, MainWindow> _detachedEditors = new();
    private Control? _workspaceEditor;
    private Border? _detachedPageNotice;
    private Button? _activateDetachedEditor;
    private bool _closingWorkspace;
    private bool IsDetachedEditor => _workspaceOwner is not null;
    private bool IsPageDetached => !IsDetachedEditor && _activePageId is { } id && _detachedEditors.ContainsKey(id);

    private Control WrapEditorForDetachment(Control editor)
    {
        _workspaceEditor = editor;
        var content = new StackPanel
        {
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new TextBlock { Text = "別ウィンドウで編集中", Foreground = WorkspaceColors.Text });
        _activateDetachedEditor = new Button { Name = "ActivateDetachedEditor", Content = "ウィンドウを前面に出す" };
        _activateDetachedEditor.Click += (_, _) => ActivateDetachedPage();
        content.Children.Add(_activateDetachedEditor);
        _detachedPageNotice = new Border
        {
            Name = "DetachedPageNotice", Background = WorkspaceColors.Editor,
            IsVisible = false, Child = content
        };
        var grid = new Grid();
        grid.Children.Add(editor);
        grid.Children.Add(_detachedPageNotice);
        return grid;
    }

    private void UpdateDetachedPageNotice()
    {
        if (_workspaceEditor is null || _detachedPageNotice is null) return;
        _workspaceEditor.IsVisible = !IsPageDetached;
        _workspaceEditor.IsEnabled = !IsPageDetached;
        _detachedPageNotice.IsVisible = IsPageDetached;
    }

    private Control BuildDetachedEditorLayout(Control editor)
    {
        // Keep search and rendering inside this window, without workspace
        // actions or changes to the workspace's persisted page selection.
        _searchScope.ItemsSource = new[] { "表示中のページ" };
        _searchScope.SelectedIndex = 0;
        _searchScope.IsEnabled = false;
        BuildSearchResultsPane();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,0") };
        grid.Children.Add(editor);
        Grid.SetColumn(_searchResultsPane, 1);
        grid.Children.Add(_searchResultsPane);
        void UpdateResultsWidth()
        {
            MinWidth = MinimumEditorWidth + (_searchResultsPane.IsVisible ? MinimumSearchResultsWidth : 0);
            grid.ColumnDefinitions[1].Width = new GridLength(_searchResultsPane.IsVisible ? MinimumSearchResultsWidth : 0);
        }
        _searchResultsPane.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty) UpdateResultsWidth();
        };
        OpenPage(_detachedPageId);
        _noteId = _store.Items().FirstOrDefault(p => p.Id == _activePageId)?.NoteId;
        return grid;
    }

    private static void BringEditorToFront(MainWindow window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        window.FocusEditingSurface();
    }

    private void ActivateDetachedPage()
    {
        if (_activePageId is { } id && _detachedEditors.TryGetValue(id, out var window)) BringEditorToFront(window);
    }

    private void OpenDetachedEditor(long id)
    {
        if (IsDetachedEditor || _closingWorkspace || _switchingWorkspace) return;
        if (_detachedEditors.TryGetValue(id, out var existing)) { BringEditorToFront(existing); return; }
        if (!_vimInputTail.IsCompleted || _workspaceShortcutRunning || OwnedWindows.Any(w => w.IsVisible)) return;
        FinishBlockInsert();
        if (!SaveCurrent() || !_store.Items().Any(p => p.Id == id && !p.IsNote)) return;
        MainWindow? window = null;
        try
        {
            var caret = _mode == EditorMode.Insert ? _editor.CaretOffset : _caret;
            var mode = _mode;
            window = new MainWindow(_store.DatabasePath, this, id);
            _detachedEditors.Add(id, window);
            if (_activePageId == id)
            {
                window._caret = window._editor.CaretOffset = Math.Clamp(caret, 0, window._pageContent.TextLength);
                window.SwitchMode(mode, focusEditor: false);
                OpenPage(id);
            }
            // A non-modal, independent window allows normal workspace actions
            // and avoids treating an editor as a blocking dialog.
            window.Show();
            BringEditorToFront(window);
        }
        catch (Exception ex)
        {
            if (window is not null) window.Close();
            _detachedEditors.Remove(id);
            if (_activePageId == id) OpenPage(id);
            _saveStatus.Text = "別ウィンドウを開けませんでした: " + ex.Message;
        }
    }

    private bool SaveAllEditors()
    {
        var workspace = _workspaceOwner ?? this;
        if (!workspace.SaveCurrent()) return false;
        foreach (var window in workspace._detachedEditors.Values.ToArray())
        {
            if (window.SaveCurrent()) continue;
            BringEditorToFront(window);
            return false;
        }
        return true;
    }

    private bool PrepareEditorWindowsForClose()
    {
        if (IsDetachedEditor)
        {
            FinishBlockInsert();
            return SaveCurrent();
        }
        FinishBlockInsert();
        foreach (var window in _detachedEditors.Values) window.FinishBlockInsert();
        if (!SaveAllEditors()) return false;
        // Do not discard a dialog in another window.
        var busy = _detachedEditors.Values.FirstOrDefault(w => w.OwnedWindows.Any(d => d.IsVisible));
        if (busy is not null) { BringEditorToFront(busy); return false; }
        _closingWorkspace = true;
        foreach (var window in _detachedEditors.Values.ToArray()) window.Close();
        if (_detachedEditors.Count == 0) return true;
        _closingWorkspace = false;
        return false;
    }

    private void DetachedEditorClosed()
    {
        if (_workspaceOwner is not { } workspace || _detachedPageId is not { } id) return;
        workspace._detachedEditors.Remove(id);
        if (workspace._closingWorkspace) return;
        if (workspace._activePageId == id)
        {
            var caret = _mode == EditorMode.Insert ? _editor.CaretOffset : _caret;
            workspace.OpenPage(id);
            if (workspace._activePageId == id)
            {
                workspace._caret = workspace._editor.CaretOffset = Math.Clamp(caret, 0, workspace._pageContent.TextLength);
                workspace.SwitchMode(_mode, focusEditor: false);
                workspace.Refresh();
            }
        }
        workspace.ReloadLists();
    }

    private void RefreshDetachedPageMetadata()
    {
        var workspace = _workspaceOwner ?? this;
        var pages = workspace._store.Items();
        foreach (var (id, window) in workspace._detachedEditors.ToArray())
        {
            var page = pages.FirstOrDefault(p => p.Id == id && !p.IsNote);
            if (page is null)
            {
                // Deletion saves every editor before updating the database.
                // Never write stale contents back after a page is deleted.
                window.Close();
                continue;
            }
            window.Title = FormatPageWindowTitle(page.Name);
            window._noteId = page.NoteId;
        }
        if (workspace._activePageId is { } active && pages.FirstOrDefault(p => p.Id == active) is { } selected)
            workspace.Title = FormatPageWindowTitle(selected.Name);
        workspace.ReloadLists();
    }
}
