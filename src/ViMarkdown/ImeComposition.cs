using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace ViMarkdown;

// Keep marked text outside TextDocument: only TextInput commits to the document.
internal sealed class ImeComposition : TextInputMethodClient
{
    private readonly TextArea _area;
    private readonly CompositionLayer _layer;
    private TextInputMethodClient? _inner;
    private string _text = "";
    private int _cursor;
    private TextDocument? _document;
    public bool IsComposing => _text.Length > 0;

    public ImeComposition(TextArea area)
    {
        _area = area;
        _layer = new CompositionLayer(this) { IsHitTestVisible = false };
        area.TextView.InsertLayer(_layer, KnownLayer.Caret, LayerInsertionPosition.Above);
        area.AddHandler(InputElement.TextInputMethodClientRequestedEvent, (_, e) =>
        {
            if (e.Client is null || ReferenceEquals(e.Client, this)) return;
            if (!ReferenceEquals(_inner, e.Client))
            {
                Detach();
                _inner = e.Client;
                _inner.CursorRectangleChanged += CursorChanged;
                _inner.SurroundingTextChanged += SurroundingChanged;
                _inner.SelectionChanged += SelectionChangedHandler;
                _inner.TextViewVisualChanged += VisualChanged;
                _inner.ResetRequested += Reset;
            }
            e.Client = this;
        }, RoutingStrategies.Bubble);
        area.AddHandler(InputElement.TextInputEvent, (_, _) => Clear(), RoutingStrategies.Tunnel);
        area.LostFocus += (_, _) => Cancel();
        void TrackDocument()
        {
            if (_document is not null) _document.TextChanged -= DocumentEdited;
            _document = area.Document;
            if (_document is not null) _document.TextChanged += DocumentEdited;
            Cancel();
        }
        area.DocumentChanged += (_, _) => TrackDocument();
        TrackDocument();
        area.TextView.ScrollOffsetChanged += (_, _) => CursorChanged(this, EventArgs.Empty);
    }

    private void DocumentEdited(object? sender, EventArgs e) => Cancel();

    private void Detach()
    {
        if (_inner is null) return;
        _inner.CursorRectangleChanged -= CursorChanged;
        _inner.SurroundingTextChanged -= SurroundingChanged;
        _inner.SelectionChanged -= SelectionChangedHandler;
        _inner.TextViewVisualChanged -= VisualChanged;
        _inner.ResetRequested -= Reset;
    }
    private void CursorChanged(object? sender, EventArgs e) { _layer.InvalidateVisual(); RaiseCursorRectangleChanged(); }
    private void SurroundingChanged(object? sender, EventArgs e) => RaiseSurroundingTextChanged();
    private void SelectionChangedHandler(object? sender, EventArgs e) => RaiseSelectionChanged();
    private void VisualChanged(object? sender, EventArgs e) => RaiseTextViewVisualChanged();
    private void Reset(object? sender, EventArgs e) { Clear(); RequestReset(); }
    public void Clear() => SetPreeditText("", 0);
    public void Cancel() { if (!IsComposing) return; Clear(); RequestReset(); }
    public override Visual TextViewVisual => _inner?.TextViewVisual ?? _area.TextView;
    public override bool SupportsPreedit => true;
    public override bool SupportsSurroundingText => _inner?.SupportsSurroundingText ?? false;
    public override string SurroundingText => _inner?.SurroundingText ?? "";
    public override TextSelection Selection
    {
        get => _inner?.Selection ?? default;
        set { if (_inner is not null) _inner.Selection = value; }
    }
    public override void ExecuteContextMenuAction(ContextMenuAction action) => _inner?.ExecuteContextMenuAction(action);
    public override void SetPreeditText(string? text) => SetPreeditText(text, null);
    public override void SetPreeditText(string? text, int? cursorOffset)
    {
        _text = text ?? "";
        _cursor = Math.Clamp(cursorOffset ?? _text.Length, 0, _text.Length);
        _layer.InvalidateVisual();
        RaiseCursorRectangleChanged();
    }
    private TextLayout Layout() => new(_text, new Typeface(_area.FontFamily), _area.FontSize,
        _area.Foreground, maxWidth: Math.Max(1, _area.TextView.Bounds.Width - Origin().X),
        textWrapping: TextWrapping.Wrap);
    private Point Origin()
    {
        var rect = _inner?.CursorRectangle ?? default;
        var origin = TextViewVisual.TranslatePoint(rect.TopLeft, _area.TextView) ?? rect.TopLeft;
        // At a wrapped line end, start composition on the following line.
        return _area.TextView.Bounds.Width - origin.X < _area.FontSize
            ? new Point(0, origin.Y + Math.Max(rect.Height, _area.FontSize)) : origin;
    }
    public override Rect CursorRectangle
    {
        get
        {
            var original = _inner?.CursorRectangle ?? default;
            if (!IsComposing) return original;
            using var layout = Layout();
            var cursor = layout.HitTestTextPosition(_cursor);
            var origin = _area.TextView.TranslatePoint(Origin(), TextViewVisual) ?? original.TopLeft;
            return new Rect(origin.X + cursor.X, origin.Y + cursor.Y, 1, cursor.Height);
        }
    }
    private sealed class CompositionLayer(ImeComposition owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            if (!owner.IsComposing) return;
            var origin = owner.Origin();
            using var layout = owner.Layout();
            context.FillRectangle(WorkspaceColors.Editor,
                new Rect(origin, new Size(Math.Max(1, layout.Width), layout.Height)));
            layout.Draw(context, origin);
            var pen = new Pen(owner._area.Foreground ?? Brushes.Black, 1);
            var y = origin.Y;
            foreach (var line in layout.TextLines)
            {
                context.DrawLine(pen, new Point(origin.X, y + line.Height - 1),
                    new Point(origin.X + line.Width, y + line.Height - 1));
                y += line.Height;
            }
            var caret = layout.HitTestTextPosition(owner._cursor);
            context.DrawLine(pen, origin + new Vector(caret.X, caret.Y),
                origin + new Vector(caret.X, caret.Bottom));
        }
    }
}
