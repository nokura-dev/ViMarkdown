using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace ViMarkdown;

public sealed partial class RichTextLayout
{
    public void DrawBackground(DrawingContext context, Rect? viewport = null)
    {
        foreach (var item in _mapping.Where(m => m.Unit.Code))
        {
            var first = DisplayLine(item.Start);
            var last = DisplayLine(item.Start + Math.Max(0, item.Length - 1));
            if (!Visible(_lineTops[first], _lineTops[last] + _lineHeights[last] - _lineTops[first], viewport)) continue;
            foreach (var rect in TextRangeBounds(item.Start, item.Length))
                if (Visible(rect.Top, rect.Height, viewport))
                    context.FillRectangle(WorkspaceColors.CodeBackground, rect);
        }
        foreach (var obj in _objects.Where(o => o.Unit.Table is not null && Visible(o.Top, o.Height, viewport)))
            foreach (var (cell, bounds) in obj.Cells)
            {
                var origin = CellOrigin(obj, cell, bounds);
                using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
                    cell.DrawBackground(context, viewport?.Translate(-origin));
            }
    }

    public void Draw(DrawingContext context, Rect? viewport = null)
    {
        for (var i = 0; i < Layout.TextLines.Count; i++)
            if (Visible(_lineTops[i], _lineHeights[i], viewport))
                Layout.TextLines[i].Draw(context, new Point(0, _lineTops[i]));
        foreach (var heading in _mapping.Where(m => m.Unit.HeadingRuleThickness > 0))
        {
            var thickness = heading.Unit.HeadingRuleThickness;
            var index = DisplayLine(heading.Start);
            var y = _lineTops[index] + _lineHeights[index] - thickness / 2;
            if (!Visible(y, thickness, viewport)) continue;
            context.DrawLine(new Pen(WorkspaceColors.Divider, thickness), new Point(0, y), new Point(_width, y));
        }
        foreach (var obj in _objects)
        {
            if (!Visible(obj.Top, obj.Height, viewport)) continue;
            var top = obj.Top;
            if (obj.Unit.Rule)
                context.DrawLine(new Pen(WorkspaceColors.Divider, 1), new Point(0, top + obj.Height / 2), new Point(_width, top + obj.Height / 2));
            else if (obj.Unit.Table is not null)
            {
                foreach (var (cell, rect) in obj.Cells)
                {
                    var bounds = rect.Translate(new Vector(0, top));
                    context.DrawRectangle(null, new Pen(WorkspaceColors.Divider), bounds);
                    var origin = CellOrigin(obj, cell, rect);
                    using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
                        cell.Draw(context, viewport?.Translate(-origin));
                }
            }
            else if (obj.Image is { } image)
            {
                context.DrawImage(image, new Rect(new Point(ImageLeftPadding, top), obj.ImageSize));
            }
            else
            {
                using var label = new TextLayout(obj.Error ?? "[画像を読み込めません]", Typography.BodyTypeface, Typography.Diagnostic, WorkspaceColors.Diagnostic,
                    textWrapping: TextWrapping.Wrap, maxWidth: Math.Max(1, _width - 16));
                label.Draw(context, new Point(8, top + (obj.Pending || obj.Error is null ? 8 : 0)));
            }
        }
    }

}
