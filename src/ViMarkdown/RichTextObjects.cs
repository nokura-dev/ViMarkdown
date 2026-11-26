using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;

namespace ViMarkdown;

public sealed partial class RichTextLayout
{
    // Measure an atomic object and reserve its display lines. Diagnostics and D2
    // images retain their exact height; other objects occupy whole body lines.
    private (ObjectLayout Layout, int Lines) CreateObjectLayout(DisplayUnit unit, int offset, double objectLineHeight,
        Func<string, byte[]?>? imageResolver, double lineHeight, Func<string, D2Result?>? d2Resolver)
    {
        var cells = new List<(RichTextLayout Layout, Rect Rect)>();
        Bitmap? bitmap = null;
        var imageSize = default(Size);
        double height = 84;
        string? error = null;
        var pending = false;
        if (unit.Table is { Count: > 0 } rows)
        {
            var columns = Math.Max(1, rows.Max(r => r.Count));
            var cellWidth = _width / columns;
            height = 0;
            foreach (var row in rows)
            {
                var layouts = row.Select((c, i) => new RichTextLayout(c, Math.Max(1, cellWidth - CellPaddingX * 2), imageResolver, lineHeight, d2Resolver, unit.Alignments?.ElementAtOrDefault(i) ?? TextAlignment.Left)).ToArray();
                var rowHeight = layouts.Select(l => l.Height).DefaultIfEmpty(objectLineHeight).Max() + CellPaddingY * 2;
                for (var i = 0; i < layouts.Length; i++)
                    cells.Add((layouts[i], new Rect(i * cellWidth, height, cellWidth, rowHeight)));
                height += rowHeight;
            }
        }
        else if (unit.Rule) height = objectLineHeight;
        else
        {
            try
            {
                var result = unit.D2Code is null ? null : d2Resolver?.Invoke(unit.D2Code);
                pending = unit.D2Code is not null && result is null;
                error = unit.D2Code is null ? null : result?.Error ?? (result?.Png is null ? "D2を描画中…" : null);
                var data = unit.D2Code is null ? imageResolver?.Invoke(unit.ImageUrl!) : result?.Png;
                if (data is not null) bitmap = new Bitmap(new MemoryStream(data));
                if (bitmap is not null)
                {
                    imageSize = ImageContent.DisplaySize(bitmap, data!, Math.Max(1, _width - ImageLeftPadding));
                    if (unit.ImageUrl is not null && unit.D2Code is null &&
                        _imageWidths?.TryGetValue(unit.Start, out var temporaryWidth) == true)
                    {
                        var width = Math.Clamp(temporaryWidth, 1, Math.Max(1, _width - ImageLeftPadding));
                        imageSize = new Size(width, width * bitmap.PixelSize.Height / bitmap.PixelSize.Width);
                    }
                    height = imageSize.Height + (unit.D2Code is null ? 12 : 0);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { }
        }
        if (error is not null && !pending)
        {
            using var label = new TextLayout(error, Typography.BodyTypeface, Typography.Diagnostic, WorkspaceColors.Diagnostic,
                textWrapping: TextWrapping.Wrap, maxWidth: Math.Max(1, _width - 16));
            height = label.Height + objectLineHeight;
        }
        var objectLines = (int)Math.Ceiling(height / objectLineHeight);
        if (error is null && !(unit.D2Code is not null && bitmap is not null))
            height = objectLines * objectLineHeight;
        return (new(unit, offset, height, bitmap, imageSize, cells, error, pending), objectLines);
    }

    public DisplayUnit? ImageAt(Point point)
    {
        foreach (var obj in _objects)
        {
            if (obj.Unit.Table is not null)
            {
                foreach (var (cell, bounds) in obj.Cells)
                {
                    var image = cell.ImageAt(point - CellOrigin(obj, cell, bounds));
                    if (image is not null) return image;
                }
            }
            else if (obj.Image is not null &&
                new Rect(new Point(ImageLeftPadding, obj.Top), obj.ImageSize).Contains(point))
                return obj.Unit;
        }
        return null;
    }

    public IEnumerable<(int Offset, Rect Bounds)> ResizableImages()
    {
        foreach (var obj in _objects)
            if (obj.Image is not null && obj.Unit.ImageUrl is not null && obj.Unit.D2Code is null)
                yield return (obj.Unit.Start, new Rect(new Point(ImageLeftPadding, obj.Top), obj.ImageSize));
    }
}
