using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ViMarkdown;

public sealed class RichSurface : Control
{
    private double _bodyLineHeight = double.NaN;
    public double BodyLineHeight
    {
        get => _bodyLineHeight;
        set
        {
            if (_bodyLineHeight.Equals(value)) return;
            _bodyLineHeight = value;
            ResetLayout();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }
    private Thickness _contentPadding = new(24);
    public Thickness ContentPadding
    {
        get => _contentPadding;
        set
        {
            if (_contentPadding == value) return;
            _contentPadding = value;
            ResetLayout();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }
    // All drawing, hit testing, selection and scrolling share this origin.
    public Point ContentOrigin
    {
        get
        {
            var layout = _layout ?? EnsureLayout(Bounds.Width > 0 ? Bounds.Width : 900);
            var inset = layout.FirstLineInkInset;
            return new Point(ContentPadding.Left - inset.X, ContentPadding.Top - inset.Y);
        }
    }
    private static readonly IBrush BackgroundBrush = WorkspaceColors.Editor;
    private static readonly IBrush SelectionBrush = WorkspaceColors.Selection;
    private IReadOnlyList<DisplayUnit> _units = [];
    private RichTextLayout? _layout;
    private string? _source;
    private DisplayNavigation _navigation = new("", []);
    private double _layoutWidth;
    private readonly Dictionary<string, MermaidResult> _diagrams = [];
    private int _generation;
    public Func<string, byte[]?>? ImageResolver { get; set; }
    public int Caret { get; set; }
    public int? Anchor { get; set; }
    public IReadOnlyList<(int Start, int End)> SearchRanges { get; set; } = [];
    public IReadOnlyList<(int Start, int End)>? SelectionRanges { get; set; }
    public event Action<int>? PositionPicked;
    public event Action<int>? TaskToggled;
    public RichSurface() { Focusable = true; ClipToBounds = true; }

    public void SetSource(string source)
    {
        if (_source != source)
        {
            _source = source;
            _units = Projection.Build(source);
            _navigation = new DisplayNavigation(source, _units);
            _diagrams.Clear();
            var generation = ++_generation;
            foreach (var code in _units.Where(u => u.MermaidCode is not null).Select(u => u.MermaidCode!).Distinct())
                _ = LoadDiagram(code, generation);
            ResetLayout();
            InvalidateMeasure();
        }
        InvalidateVisual();
    }

    private async Task LoadDiagram(string code, int generation)
    {
        var result = await MermaidRenderer.Shared.RenderAsync(code).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _generation) return;
            _diagrams[code] = result;
            ResetLayout();
            InvalidateMeasure();
            InvalidateVisual();
        });
    }

    private void ResetLayout()
    {
        _layout?.Dispose();
        _layout = null;
    }

    private RichTextLayout EnsureLayout(double width)
    {
        var textWidth = Math.Max(1, width - ContentPadding.Left - ContentPadding.Right);
        if (_layout is null || _layoutWidth != textWidth)
        {
            ResetLayout();
            _layoutWidth = textWidth;
            _layout = new RichTextLayout(_units, textWidth, ImageResolver,
                // Larger runs retain automatic height so headings and checkboxes fit.
                lineHeight: _units.All(unit => unit.Size <= Typography.Body) ? BodyLineHeight : double.NaN,
                mermaidResolver: code => _diagrams.GetValueOrDefault(code));
        }
        return _layout;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 900;
        var layout = EnsureLayout(width);
        return new Size(width, layout.Height + ContentOrigin.Y + ContentPadding.Bottom);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var layout = EnsureLayout(Bounds.Width);
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));
        using (context.PushTransform(Matrix.CreateTranslation(ContentOrigin.X, ContentOrigin.Y)))
        {
            foreach (var range in SearchRanges)
                foreach (var rect in layout.SelectionBounds(range.Start, range.End))
                    context.FillRectangle(WorkspaceColors.SearchMatch, rect);
            foreach (var range in SelectionRanges ?? (Anchor.HasValue ? new[] { (Anchor.Value, Caret) } : Array.Empty<(int, int)>()))
                foreach (var rect in layout.SelectionBounds(range.Item1, range.Item2))
                    context.FillRectangle(SelectionBrush, rect);
            layout.Draw(context);
            context.FillRectangle(WorkspaceColors.Accent, layout.CaretBounds(Caret));
        }
    }

    public Rect CaretBounds => EnsureLayout(Bounds.Width).CaretBounds(Caret)
        .Translate(new Vector(ContentOrigin.X, ContentOrigin.Y));

    public void BringCaretIntoView()
    {
        var layout = EnsureLayout(Bounds.Width);
        var caret = layout.CaretBounds(Caret);
        var target = caret.Translate(new Vector(ContentOrigin.X, ContentOrigin.Y)).Inflate(4);
        // Include the document margins at the first/last displayed line so the
        // scrollbar reaches its endpoint rather than leaving trailing space.
        var firstLine = layout.Layout.GetLineIndexFromCharacterIndex(layout.ToDisplayOffset(Caret), false) == 0;
        if (firstLine)
            target = new Rect(target.X, 0, target.Width, target.Bottom);
        if (layout.IsOnLastContentLine(Caret))
            target = new Rect(target.X, target.Y, target.Width,
                Math.Max(target.Height, layout.Height + ContentOrigin.Y + ContentPadding.Bottom - target.Y));
        this.BringIntoView(target);
        // Fractional font metrics can leave a subpixel offset after the routed
        // request. The first line includes the top margin, so restore its exact
        // scroll endpoint rather than accumulating that rounding remainder.
        if (firstLine && this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is { } scroll &&
            target.Height <= scroll.Viewport.Height)
            scroll.Offset = new Vector(scroll.Offset.X, 0);
    }

    public int NormalizePosition(int offset) => _navigation.Normalize(offset);

    public int PreviousDisplayedCharacter(int offset) => _navigation.PreviousInLine(offset);

    public int MoveCaretHorizontal(int offset, int direction) =>
        TryMoveInTable(offset, direction, false, out var target) ? target : _navigation.CaretHorizontal(offset, direction);

    public double NavigationX(int offset) => EnsureLayout(Bounds.Width).CaretBounds(offset).X;

    public int MoveDisplayed(int offset, string motion, int count, double? desiredX = null)
    {
        offset = _navigation.Normalize(offset);
        if (motion is "h" or "l")
        {
            for (var n = 0; n < count; n++)
                offset = TryMoveInTable(offset, motion == "h" ? -1 : 1, false, out var moved)
                    ? moved : _navigation.Horizontal(offset, motion == "h" ? -1 : 1, 1);
            return offset;
        }
        if (motion is "j" or "k")
        {
            for (var n = 0; n < count; n++)
            {
                var raw = offset;
                // Spacers and fences may map to the same visible position.
                var seen = new HashSet<int> { raw };
                while (true)
                {
                    raw = EnsureLayout(Bounds.Width).MoveVertical(raw, motion == "j" ? 1 : -1, desiredX);
                    if (!seen.Add(raw)) break;
                    var target = _navigation.Normalize(raw);
                    var layout = EnsureLayout(Bounds.Width);
                    var deltaY = layout.CaretBounds(target).Center.Y - layout.CaretBounds(offset).Center.Y;
                    if (target == offset || (motion == "j" ? deltaY <= .1 : deltaY >= -.1)) continue;
                    offset = target;
                    break;
                }
            }
            return offset;
        }
        if (motion is "0" or "^" or "$")
            return _navigation.Boundary(offset, motion == "$");
        return _navigation.Word(offset, motion[0], count);
    }

    public bool TryMoveInTable(int offset, int direction, bool vertical, out int target)
    {
        var moved = EnsureLayout(Bounds.Width).TryMoveInTable(offset, direction, vertical, out target);
        if (moved && !vertical && direction > 0)
            target = _navigation.AtOrAfter(target);
        return moved;
    }

    public int MoveVertical(int offset, int direction)
        => EnsureLayout(Bounds.Width).MoveVertical(offset, direction);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetPosition(this);
        var point = new Point(p.X - ContentOrigin.X, p.Y - ContentOrigin.Y);
        var layout = EnsureLayout(Bounds.Width);
        var interactive = layout.InteractiveAt(point);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && interactive?.TaskOffset is int offset)
            TaskToggled?.Invoke(offset);
        else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && interactive?.LinkUrl is string url &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto")
            _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri);
        else PositionPicked?.Invoke(layout.HitTest(point));
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ResetLayout();
        base.OnDetachedFromVisualTree(e);
    }
}
