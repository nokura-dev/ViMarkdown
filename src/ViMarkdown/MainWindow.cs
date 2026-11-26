using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace ViMarkdown;

public enum EditorMode { Normal, Visual, Insert }

public sealed partial class MainWindow : Window
{
    private readonly TextDocument _pageContent = new();
    private readonly TextEditor _editor;
    private readonly SourceNavigation _source;
    private readonly ImeComposition _ime;
    private readonly RichSurface _rich = new();
    private readonly ScrollViewer _preview;
    private readonly TextBlock _status = new();
    private readonly Dictionary<EditorMode, Button> _modeButtons = new();
    private EditorMode _mode = EditorMode.Normal;
    private int _caret;
    private int? _anchor;
    private int _modeSwitchGeneration;
    private bool _aligningContent;
    private bool _sourceRefreshPending;
    public MainWindow() : this(null) { }
    public MainWindow(string? databasePath) : this(databasePath, null, null) { }
    private MainWindow(string? databasePath, MainWindow? workspaceOwner, long? pageId)
    {
        _workspaceOwner = workspaceOwner;
        _detachedPageId = pageId;
        _source = new SourceNavigation(_pageContent);
        Activated += (_, _) => MacInputMode.SynchronizeMainWindow(this);
        Deactivated += (_, _) => CancelPendingCommand();
        Closed += (_, _) => CancelPendingCommand();
        Title = "ViMarkdown";
        Width = IsDetachedEditor ? 900 : 1400; MinWidth = IsDetachedEditor ? MinimumEditorWidth : MinimumWorkspaceWidth;
        Height = 760; MinHeight = IsDetachedEditor ? 300 : MinimumWorkspaceHeight;
        Background = WorkspaceColors.Editor;
        Foreground = WorkspaceColors.Text;
        _editor = CreateSourceEditor();
        _editor.TextArea.TextView.SizeChanged += (_, e) =>
        {
            if (!_aligningContent && _editor.IsVisible && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > .01)
                Dispatcher.UIThread.Post(SyncRichContentPadding, DispatcherPriority.Loaded);
        };
        _ime = new ImeComposition(_editor.TextArea);
        _preview = new ScrollViewer { Background = WorkspaceColors.Editor, Content = _rich, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Content = BuildWorkspace(BuildEditorPane(), databasePath);
        _rich.TaskToggled += offset =>
        {
            CancelPendingCommand();
            if (offset >= 0 && offset + 2 < _pageContent.TextLength && _pageContent.GetCharAt(offset) == '[')
            {
                _pageContent.Replace(offset + 1, 1, _pageContent.GetCharAt(offset + 1) == ' ' ? "x" : " ");
                _caret = offset;
                Refresh();
            }
        };
        _rich.PositionPicked += position => { _verticalDisplayX = null; CancelPendingCommand(); _caret = position; Refresh(); };
        _pageContent.TextChanged += OnPageContentChanged;
        _editor.TextArea.Caret.PositionChanged += (_, _) => { if (_mode == EditorMode.Insert) UpdateStatus(); };
        AddHandler(KeyDownEvent, HandleKey, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyUpEvent, HandleLayoutKeyUp, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, HandleVimText, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, (_, _) =>
        {
            if (!_insertInputGroupOpen) return;
            _pageContent.UndoStack.EndUndoGroup(); _insertInputGroupOpen = false;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, HandleVimPromptKey, RoutingStrategies.Tunnel);
        Opened += (_, _) => { SyncRichContentPadding(); Refresh(); FocusEditingSurface(); };
    }
    private void OnPageContentChanged(object? sender, EventArgs e)
    {
        ClearWorkspaceHighlights();
        CancelPendingCommand();
        _caret = Math.Min(_caret, _pageContent.TextLength);
        // Refresh syntax colors and the shared padding before the next frame.
        if (!_sourceRefreshPending)
        {
            _sourceRefreshPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _sourceRefreshPending = false;
                // Coalesce edits and page changes; refresh the current page only.
                _editor.TextArea.TextView.Redraw();
                SyncRichContentPadding();
            }, DispatcherPriority.Loaded);
        }
        if (!_loading && _mode != EditorMode.Insert) _rich.SetSource(_pageContent.Text);
        UpdateStatus();
        ScheduleSave();
    }

    private void ApplyHistory(bool redo)
    {
        CancelPendingCommand();
        _continueInsertUndo = false;
        _blockInsertStart = null; _blockInsertTargets = null;
        var history = _pageContent.UndoStack;
        // An empty history must not replace the normal-mode position with the
        // hidden editor's (possibly older) position.
        if (redo ? !history.CanRedo : !history.CanUndo) return;
        if (_mode != EditorMode.Insert)
            _editor.Select(_caret, 0);
        if (redo) history.Redo();
        else history.Undo();
        // TextArea restores its caret together with the document's undo group.
        // Copy it only after the complete operation, not during TextChanged.
        _caret = _editor.CaretOffset;
        _anchor = _mode == EditorMode.Visual ? _caret : null;
        if (_mode != EditorMode.Insert)
            _editor.Select(_caret, 0);
        Refresh();
    }
    private void Refresh()
    {
        _caret = Math.Clamp(_caret, 0, _pageContent.TextLength);
        if (_mode != EditorMode.Insert)
        {
            _rich.SetSource(_pageContent.Text);
            if (_caret != _source.LineAt(_caret).EndOffset)
                _caret = _rich.NormalizePosition(_caret);
        }
        _rich.Caret = _caret; _rich.Anchor = null;
        _rich.SelectionRanges = _mode == EditorMode.Visual ? VimSelection() : null;
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        foreach (var (mode, button) in _modeButtons)
        {
            button.Background = mode == _mode ? WorkspaceColors.Selected : Brushes.Transparent;
            if (button.Content is Avalonia.Controls.Shapes.Path icon)
                icon.Stroke = mode == _mode ? WorkspaceColors.Accent : WorkspaceColors.Muted;
        }
        var offset = _mode == EditorMode.Insert ? _editor.CaretOffset : _caret;
        var location = _pageContent.GetLocation(Math.Clamp(offset, 0, _pageContent.TextLength));
        _status.Text = _commandPrompt is not null ? _commandPrompt : $"モード: {_mode.ToString().ToUpperInvariant()}{(_vimKeys.Length > 0 ? "  " + _vimKeys : "")}  |  行数:{_pageContent.LineCount}  |  文字数:{_pageContent.TextLength}  |  位置: {location.Column},{location.Line}";
    }
}
