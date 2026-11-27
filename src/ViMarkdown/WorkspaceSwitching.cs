using Avalonia.Platform.Storage;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private bool _switchingWorkspace;

    private async Task SwitchWorkspaceAsync()
    {
        if (IsDetachedEditor || _switchingWorkspace || OwnedWindows.Any(w => w.IsVisible)) return;
        _switchingWorkspace = true;
        WorkspaceStore? candidate = null;
        string? temporary = null;
        var original = _store;
        var committed = false;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "データ保存場所の切替", AllowMultiple = false,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(
                    Path.GetDirectoryName(Path.GetFullPath(_store.DatabasePath))!)
            });
            if (folders.Count == 0) return;
            var folder = Path.GetFullPath(folders[0].TryGetLocalPath()
                ?? throw new InvalidOperationException("ローカルフォルダを選択してください。"));
            var path = Path.Combine(folder, "ViMarkdown.db");
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(Path.GetFullPath(_store.DatabasePath), path, comparison)) return;

            // Save and close every detached editor while it still uses the old DB.
            if (!PrepareEditorWindowsForClose())
            {
                _saveStatus.Text = "切替を中断しました。編集内容の保存と別ウィンドウを確認してください。";
                return;
            }
            candidate = new WorkspaceStore(path);
            candidate.PrepareEditHistory();
            InitializeWorkspace(candidate);
            _ = candidate.Items();
            temporary = WorkspaceConfiguration.Prepare(folder);

            // Keep the original connection until both the UI and config are ready.
            DisplayWorkspace(candidate);
            File.Move(temporary, WorkspaceConfiguration.ConfigPath, overwrite: true);
            temporary = null;
            committed = true;
            candidate = null; // The main window now owns this connection.
            original.Dispose();
            _saveStatus.Text = "データ保存場所を切り替えました: " + folder;
        }
        catch (Exception ex)
        {
            if (!committed && !ReferenceEquals(_store, original))
            {
                try { DisplayWorkspace(original); }
                catch (Exception restoreError)
                {
                    _saveStatus.Text = "現在のDBを維持しましたが、表示の再読込に失敗しました: " + restoreError.Message;
                    return;
                }
            }
            _saveStatus.Text = "データ保存場所の切替失敗: " + ex.Message;
        }
        finally
        {
            _closingWorkspace = false;
            _switchingWorkspace = false;
            candidate?.Dispose();
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception) { /* A temporary file must not hide the switch error. */ }
            }
        }
    }

    private void DisplayWorkspace(WorkspaceStore store)
    {
        _saveTimer.Stop();
        _historyTimer.Stop();
        _store = store;
        _rich.ImageResolver = store.GetImage;
        _dirty = false;
        _activePageId = _noteId = _automaticPageNameId = _pageRangeEndId = null;
        _rebuilding = true;
        try
        {
            _notes.ItemsSource = null;
            _pages.ItemsSource = null;
        }
        finally { _rebuilding = false; }
        ++_searchNavigationGeneration;
        _searchResults.ItemsSource = null;
        _searchResultsPane.IsVisible = false;
        _searchSummary.Text = "";
        _timelinePeriod = 0;
        _updateTimelineSelection?.Invoke();
        LoadWorkspace();
    }
}
