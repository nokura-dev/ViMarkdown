using Avalonia.Input;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private readonly Dictionary<(bool Notes, string Action), Func<Task>> _workspaceActions = new();
    private bool _workspaceShortcutRunning;

    // Add Command aliases only for application shortcuts, leaving Vim Control keys intact.
    private static KeyModifiers WorkspaceModifiers(KeyModifiers modifiers) =>
        OperatingSystem.IsMacOS() && modifiers.HasFlag(KeyModifiers.Meta) &&
        !modifiers.HasFlag(KeyModifiers.Control)
            ? (modifiers & ~KeyModifiers.Meta) | KeyModifiers.Control
            : modifiers;

    private async void HandleWorkspaceShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (!IsDetachedEditor && WorkspaceModifiers(e.KeyModifiers) == KeyModifiers.Control && e.Key is Key.D1 or Key.D2)
        {
            e.Handled = true;
            CancelPendingCommand();
            ToggleWorkspacePane(e.Key == Key.D1);
            return;
        }
        var modifiers = WorkspaceModifiers(e.KeyModifiers);
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _searchResults.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            _searchResultsPane.IsVisible = false;
            _searchKeyword.Text = "";
            _searchResults.ItemsSource = null;
            ClearWorkspaceHighlights();
            CancelPendingCommand();
            FocusEditingSurface();
            return;
        }
        if (e.Key == Key.F && modifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift))
        {
            e.Handled = true;
            CancelPendingCommand();
            var currentNote = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            _searchScope.SelectedIndex = IsDetachedEditor ? 0 : (int)(currentNote
                ? WorkspaceSearchScope.SelectedNote : WorkspaceSearchScope.CurrentPage);
            _searchKeyword.Focus();
            _searchKeyword.CaretIndex = _searchKeyword.Text?.Length ?? 0;
            _searchKeyword.SelectionStart = _searchKeyword.SelectionEnd = _searchKeyword.CaretIndex;
            return;
        }
        (bool Notes, string Action)? command = (e.Key, modifiers) switch
        {
            (Key.N, KeyModifiers.Control | KeyModifiers.Shift) => (true, "作成"),
            (Key.N, KeyModifiers.Control) => (false, "作成"),
            (Key.F2, KeyModifiers.Control) => (true, "名前変更"),
            (Key.F2, KeyModifiers.None) => (false, "名前変更"),
            (Key.Delete, KeyModifiers.None) when _notes.IsKeyboardFocusWithin => (true, "削除"),
            (Key.Delete, KeyModifiers.None) when _pages.IsKeyboardFocusWithin => (false, "削除"),
            _ => null
        };
        if (IsDetachedEditor) return;
        if (command is not { } key || !_workspaceActions.TryGetValue(key, out var action)) return;
        e.Handled = true;
        if (_workspaceShortcutRunning || OwnedWindows.Any(w => w.IsVisible)) return;
        _workspaceShortcutRunning = true;
        try { await action(); }
        finally { _workspaceShortcutRunning = false; }
    }
}
