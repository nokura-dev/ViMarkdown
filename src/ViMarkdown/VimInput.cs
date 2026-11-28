using System.Text.RegularExpressions;
using Avalonia.Input;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private string _vimKeys = "";
    // Keep rapid sequences such as yyp ordered even on asynchronous clipboard backends.
    private Task _vimInputTail = Task.CompletedTask;
    private KeyEventArgs? _pendingLayoutKey;

    private void HandleLayoutKeyUp(object? sender, KeyEventArgs e)
    {
        if (_pendingLayoutKey is not { } pending || pending.Key != e.Key) return;
        _pendingLayoutKey = null;
        HandleVimKey(pending);
    }
    private readonly Avalonia.Controls.TextBox _commandInput = new()
    {
        Name = "VimCommandInput", IsVisible = false, AcceptsReturn = false,
        MinHeight = 0, FontSize = 12, Padding = new Avalonia.Thickness(0),
        Background = WorkspaceColors.Chrome, Foreground = WorkspaceColors.Text,
        BorderThickness = new Avalonia.Thickness(0), FocusAdorner = null
    };
    private readonly Avalonia.Controls.Border _commandInputRow = new() { Name = "VimCommandInputRow", IsVisible = false };
    private readonly Avalonia.Controls.TextBlock _commandInputLabel = new();
    private Avalonia.Controls.Grid _normalStatus = null!;
    private string? _commandPrompt;
    private string? _promptOpeningText;

    private void HandleVimPromptKey(object? sender, KeyEventArgs e)
    {
        if (IsDetachedEditor && e.Key == Key.W && WorkspaceModifiers(e.KeyModifiers) == KeyModifiers.Control)
        {
            e.Handled = true;
            if (!OwnedWindows.Any(w => w.IsVisible)) Close();
            return;
        }
        if (HandleSlideshowKey(e)) return;
        if (HandlePaneNavigation(e)) return;
        HandleWorkspaceShortcut(sender, e);
        if (e.Handled) return;
        if (_searchKeyword.IsKeyboardFocusWithin || _searchScope.IsKeyboardFocusWithin || _searchResults.IsKeyboardFocusWithin) return;
        if (_commandPrompt is null) return;
        _promptOpeningText = null;
        if (e.Key == Key.Escape)
        {
            if (_commandPrompt[0] != ':') _caret = _searchOrigin;
            _commandPrompt = null; FocusEditingSurface(); Refresh(); e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            if (_commandPrompt.Length > 1) _commandPrompt = _commandPrompt[..TextBoundaries.Previous(_commandPrompt, _commandPrompt.Length)];
            if (_commandPrompt.Length == 1)
            {
                if (_commandPrompt[0] != ':') _caret = _searchOrigin;
                _commandPrompt = null;
                Refresh(); FocusEditingSurface();
            }
            else
            {
                UpdateStatus(); _commandInput.CaretIndex = _commandInput.Text?.Length ?? 0;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            var command = _commandPrompt; _commandPrompt = null;
            if (command[0] == ':') ExecuteEx(command[1..]);
            else
            {
                if (command.Length > 1) _searchPattern = command[1..];
                _searchBackward = command[0] == '?'; FindMatch(_searchBackward, 1);
            }
            Refresh(); FocusEditingSurface(); e.Handled = true;
        }
        // Leave printable keys to the focused TextBox, which owns native text/IME input.
    }
    private void HandleVimText(object? sender, TextInputEventArgs e)
    {
        if (_isSlideshow) { e.Handled = true; return; }
        if (!_editor.IsKeyboardFocusWithin && !_rich.IsKeyboardFocusWithin) return;
        if (_pendingLayoutKey is { } pending && !string.IsNullOrEmpty(e.Text))
        {
            _pendingLayoutKey = null;
            HandleVimKey(new KeyEventArgs
            {
                Key = pending.Key,
                KeyModifiers = pending.KeyModifiers,
                KeySymbol = e.Text
            });
            e.Handled = true;
            return;
        }
        if (_mode == EditorMode.Insert && _commandPrompt is null && !string.IsNullOrEmpty(e.Text))
        {
            if (_continueInsertUndo) _pageContent.UndoStack.StartContinuedUndoGroup();
            else _pageContent.UndoStack.StartUndoGroup();
            _insertInputGroupOpen = true; _continueInsertUndo = true;
        }
        if (_commandPrompt is not null)
        {
            if (_promptOpeningText != e.Text) _commandPrompt += e.Text;
            _promptOpeningText = null; UpdateStatus(); e.Handled = true;
        }
        else if (_mode == EditorMode.Insert && _replaceMode && !_ime.IsComposing && !string.IsNullOrEmpty(e.Text))
        {
            var p = _editor.CaretOffset; var end = p;
            foreach (var _ in System.Globalization.StringInfo.ParseCombiningCharacters(e.Text))
                if (end < _source.LineAt(end).EndOffset) end = _source.Next(end);
            _pageContent.Replace(p, end - p, e.Text);
            _editor.CaretOffset = p + e.Text.Length; e.Handled = true;
        }
    }
    private async void HandleVimKey(KeyEventArgs e, string? nativeText = null)
    {
        // JIS punctuation may use different OEM codes. Keep native text scoped
        // to these symbols, so letter motions never inherit another event's text.
        var key = VimKeyMap.Resolve(e, nativeText);
        if ((e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None &&
            !(e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.R or Key.V)) return;
        if (e.Key != Key.Escape && key.Length == 0 &&
            !(e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.R or Key.V)) return;
        e.Handled = true;
        var previous = _vimInputTail;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _vimInputTail = completion.Task;
        var page = _activePageId;
        try
        {
            await previous;
            if (!IsVisible || page != _activePageId || _mode == EditorMode.Insert) return;
            await ProcessVimKeyAsync(e, key);
        }
        catch (Exception ex) { _saveStatus.Text = "Vim操作に失敗しました: " + ex.Message; }
        finally { completion.SetResult(); }
    }
    private async Task ProcessVimKeyAsync(KeyEventArgs e, string key)
    {
        if (_commandPrompt is not null) { e.Handled = true; return; }
        if (e.Key == Key.Escape)
        {
            if (_mode == EditorMode.Normal) HideVimSearchHighlights();
            _vimKeys = ""; SwitchMode(EditorMode.Normal); e.Handled = true; return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.R) ApplyHistory(true);
            else if (e.Key == Key.V) StartVisual(2);
            else return;
            e.Handled = true; Refresh(); return;
        }
        if ((e.KeyModifiers & ~(KeyModifiers.Shift)) != 0) return;
        if (key.Length == 0) return;
        e.Handled = true;
        _vimKeys += key;
        // Copy before executing: document changes and mode transitions cancel pending input.
        var input = _vimKeys;
        if (await ExecuteVimAsync(input)) _vimKeys = "";
        // d$ keeps the deletion start after the remaining text, just like c$.
        // Other operators retain their existing caret correction.
        ClampNormal(Regex.IsMatch(input, @"^(?:[1-9][0-9]*)?(?:[$hl]|d(?:[1-9][0-9]*)?\$)$")); Refresh();
        if (_mode != EditorMode.Insert) _rich.BringCaretIntoView();
    }
    private void HandleKey(object? sender, KeyEventArgs e)
    {
        if (_isSlideshow) { e.Handled = true; return; }
        if (!_editor.IsKeyboardFocusWithin && !_rich.IsKeyboardFocusWithin) return;
        if (_mode == EditorMode.Insert)
        {
            if (e.Key == Key.Escape)
            {
                if (_ime.IsComposing) _ime.Cancel();
                else
                {
                    FinishBlockInsert();
                    var offset = _editor.CaretOffset;
                    if (offset > _pageContent.GetLineByOffset(offset).Offset)
                        _editor.CaretOffset = TextBoundaries.Previous(_pageContent.Text, offset);
                    SwitchMode(EditorMode.Normal);
                }
                e.Handled = true;
            }
            return;
        }
        if (e.Handled) return;
        if (VimKeyMap.IsLayoutPunctuation(e.Key) && (e.KeyModifiers & ~KeyModifiers.Shift) == 0 &&
            MacInputMode.CurrentKeyText(this) is { } nativeText &&
            VimKeyMap.UsesNativeText(e.Key, nativeText))
        {
            HandleVimKey(e, nativeText);
            return;
        }
        // The JIS ^ key shares the US = key code. Let TextInput supply its
        // actual character before interpreting a missing or ambiguous symbol.
        if (e.Key == Key.OemPlus && (e.KeyModifiers & ~KeyModifiers.Shift) == 0 &&
            (string.IsNullOrEmpty(e.KeySymbol) || e.KeySymbol == "="))
        {
            _pendingLayoutKey = e;
            return;
        }
        HandleVimKey(e);
    }
    private void CancelPendingCommand() { _vimKeys = ""; _pendingLayoutKey = null; }
}
