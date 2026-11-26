using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ViMarkdown;

public sealed partial class MainWindow
{
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

    private void SwitchMode(EditorMode mode, bool focusEditor = true)
    {
        if (IsPageDetached) { if (focusEditor) _activateDetachedEditor?.Focus(); return; }
        if (_activePageId is null) { if (focusEditor) FocusEditingSurface(); return; }
        _verticalDisplayX = null;
        CancelPendingCommand();
        if (_mode == mode) { if (focusEditor) FocusEditingSurface(); return; }
        if (_mode == EditorMode.Insert) FinishBlockInsert();
        var caretY = CaretWindowY();
        if (mode == EditorMode.Insert || _mode == EditorMode.Insert) _rich.ResetImageSizes();
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
            if (focusEditor) FocusEditingSurface();
        }
        else if (focusEditor) _rich.Focus();
        Refresh();
        // Revealing a surface can create its template and change wrapping.
        // Align only after layout, and ignore callbacks from earlier switches.
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _modeSwitchGeneration || !IsVisible) return;
            UpdateLayout();
            SyncRichContentPadding();
            UpdateLayout();
            if (focusEditor) FocusEditingSurface();
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
        if (IsPageDetached) { _activateDetachedEditor?.Focus(); return; }
        if (_activePageId is null) { _notes.Focus(); return; }
        // TextEditor is a focusable wrapper; only TextArea accepts text input.
        if (_mode == EditorMode.Insert) _editor.TextArea.Focus();
        else _rich.Focus();
    }
}
