using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    // Native IME processing happens before routed key events. A routed Escape
    // cancels remaining preedit first; otherwise it exits Insert even when
    // AvaloniaEdit has handled its own Escape command.
    public MainWindow() : this(null) { }
    public MainWindow(string? databasePath)
    {

        _source = new SourceNavigation(_pageContent);
        Deactivated += (_, _) => CancelPendingCommand();
        Closed += (_, _) => CancelPendingCommand();
        Title = "ViMarkdown";
        Width = 1400; MinWidth = MinimumWorkspaceWidth; Height = 760; MinHeight = MinimumWorkspaceHeight;
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
        _pageContent.TextChanged += (_, _) =>
        {
            ClearWorkspaceHighlights();
            CancelPendingCommand();
            _caret = Math.Min(_caret, _pageContent.TextLength);
            // Refresh syntax colors and the shared padding before the next frame.
            Dispatcher.UIThread.Post(() =>
            {
                // A fence edit can change colors far beyond the edited line.
                _editor.TextArea.TextView.Redraw();
                SyncRichContentPadding();
            }, DispatcherPriority.Loaded);
            if (_mode != EditorMode.Insert) _rich.SetSource(_pageContent.Text);
            UpdateStatus();
            ScheduleSave();
        };
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
        Opened += (_, _) => { SyncRichContentPadding(); Refresh(); _rich.Focus(); };
    }
    private void SyncRichContentPadding()
    {
        if (_aligningContent) return;
        _aligningContent = true;
        try
        {
            double gutterWidth = 0;
            _rich.BodyLineHeight = _editor.TextArea.TextView.DefaultLineHeight;
            foreach (var margin in _editor.TextArea.LeftMargins)
            {
                margin.Measure(Size.Infinity);
                gutterWidth += margin.DesiredSize.Width;
            }
            // Keep the page anchor independent of the current text's font bearings.
            // The source editor's padding is adjusted separately to the same anchor.
            var basePadding = SourceEditorPadding;
            _rich.ContentPadding = new Thickness(basePadding.Left + gutterWidth, basePadding.Top, basePadding.Right, basePadding.Bottom);
            if (!_editor.IsVisible) return;
            for (var pass = 0; pass < 4; pass++)
            {
                UpdateLayout();
                var view = _editor.TextArea.TextView;
                if (view.Bounds.Width <= 0) break;
                var first = view.GetOrConstructVisualLine(_pageContent.GetLineByNumber(1)).TextLines[0];
                var inset = TextInkAlignment.Inset(first, _pageContent.Text);
                var padding = new Thickness(basePadding.Left, basePadding.Top - inset.Y, basePadding.Right, basePadding.Bottom);
                var margin = new Thickness(-inset.X, 0, 0, 0);
                if (_editor.Padding == padding && view.Margin == margin) break;
                _editor.Padding = padding;
                view.Margin = margin;
            }
        }
        finally { _aligningContent = false; }
    }

    private void SwitchMode(EditorMode mode)
    {
        _verticalDisplayX = null;
        CancelPendingCommand();
        if (_mode == mode) { FocusEditingSurface(); return; }
        if (_mode == EditorMode.Insert) FinishBlockInsert();
        var caretY = CaretWindowY();
        var atDocumentStart = (_mode == EditorMode.Insert
            ? _editor.TextArea.TextView.ScrollOffset.Y : _preview.Offset.Y) < 0.1 &&
            _pageContent.GetLineByOffset(_mode == EditorMode.Insert ? _editor.CaretOffset : _caret).LineNumber == 1;
        var generation = ++_modeSwitchGeneration;
        if (_mode == EditorMode.Visual && _anchor.HasValue)
            _lastVisual = (_anchor.Value, _caret, _visualKind);
        if (_mode == EditorMode.Insert)
        {
            _caret = _editor.CaretOffset;
            _replaceMode = false;
            _continueInsertUndo = false;
        }
        SyncRichContentPadding();
        _mode = mode;
        _anchor = mode == EditorMode.Visual ? _caret : null;
        _editor.IsVisible = mode == EditorMode.Insert;
        _preview.IsVisible = !_editor.IsVisible;
        if (mode == EditorMode.Insert)
        {
            _editor.CaretOffset = _caret;
            _editor.Select(_caret, 0);
            FocusEditingSurface();
        }
        else _rich.Focus();
        Refresh();
        // Revealing a surface can create its template and change wrapping.
        // Align only after layout, and ignore callbacks from earlier switches.
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _modeSwitchGeneration || !IsVisible) return;
            UpdateLayout();
            SyncRichContentPadding();
            UpdateLayout();
            FocusEditingSurface();
            if (_mode == EditorMode.Insert)
            {
                // Reveal the target line before aligning: virtualization initially
                // estimates the heights of wrapped lines outside the viewport.
                _editor.TextArea.Caret.BringCaretToView();
                UpdateLayout();
                // AvaloniaEdit 11.3's ScrollToVerticalOffset is a no-op.
                var scroll = _editor.TextArea.TextView.GetVisualAncestors().OfType<ScrollViewer>().First();
                for (var pass = 0; pass < 4; pass++)
                {
                    var delta = CaretWindowY() - caretY;
                    if (!atDocumentStart && Math.Abs(delta) < 1) break;
                    var previous = scroll.Offset;
                    scroll.Offset = new Vector(previous.X, atDocumentStart ? 0 : previous.Y + delta);
                    UpdateLayout();
                    if (scroll.Offset == previous) break;
                }
            }
            else
                _preview.Offset = new Vector(_preview.Offset.X,
                    atDocumentStart ? 0 : _preview.Offset.Y + CaretWindowY() - caretY);
        }, DispatcherPriority.Loaded);
    }

    private double CaretWindowY()
    {
        if (_mode != EditorMode.Insert)
            return _rich.TranslatePoint(_rich.CaretBounds.Center, this)?.Y ?? 0;
        var view = _editor.TextArea.TextView;
        var point = view.GetVisualPosition(_editor.TextArea.Caret.Position,
            AvaloniaEdit.Rendering.VisualYPosition.LineMiddle);
        return view.TranslatePoint(point - view.ScrollOffset, this)?.Y ?? 0;
    }
    private void FocusEditingSurface()
    {
        // TextEditor is a focusable wrapper; only TextArea accepts text input.
        if (_mode == EditorMode.Insert) _editor.TextArea.Focus();
        else _rich.Focus();
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
