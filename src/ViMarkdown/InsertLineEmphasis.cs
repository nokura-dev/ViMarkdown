using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace ViMarkdown;

// Keep the inherited margin's line-selection behavior, while rendering the
// active number in bold. Reserve the same four-digit width for every page so
// neither caret movement nor a different line count shifts the text origin.
internal sealed class ActiveLineNumberMargin(TextArea area) : LineNumberMargin
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var normal = base.MeasureOverride(availableSize);
        using var bold = Number("9999", true);
        return new Size(Math.Ceiling(bold.Width), normal.Height);
    }

    private TextLayout Number(string text, bool active) => new(text,
        new Typeface(Typography.EditorFont, FontStyle.Normal, active ? FontWeight.Bold : FontWeight.Normal),
        Typography.LineNumber, WorkspaceColors.Muted);

    public override void Render(DrawingContext context)
    {
        if (TextView is not { VisualLinesValid: true } view) return;
        foreach (var visual in view.VisualLines)
        {
            var number = visual.FirstDocumentLine.LineNumber;
            using var text = Number(number.ToString(CultureInfo.InvariantCulture), number == area.Caret.Line);
            var y = visual.VisualTop - view.ScrollOffset.Y + visual.TextLines[0].Baseline - text.TextLines[0].Baseline;
            // Very large documents still keep the shared origin. Fit longer
            // numbers inside the reserved width instead of widening the gutter.
            var scale = text.Width > Bounds.Width ? Bounds.Width / text.Width : 1;
            using (context.PushTransform(Matrix.CreateScale(scale, 1) * Matrix.CreateTranslation(Bounds.Width - text.Width * scale, y)))
                text.Draw(context, default);
        }
    }
}

internal sealed class InsertLineUnderline(TextArea area) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid) return;
        var visual = textView.GetVisualLine(area.Caret.Line);
        if (visual is null) return;
        var line = visual.GetTextLine(area.Caret.VisualColumn, area.Caret.Position.IsAtEndOfLine);
        var y = visual.GetTextLineVisualYPosition(line, VisualYPosition.LineBottom) - textView.ScrollOffset.Y - .5;
        drawingContext.DrawLine(new Pen(WorkspaceColors.Divider, 1), new Point(0, y), new Point(textView.Bounds.Width, y));
    }
}
