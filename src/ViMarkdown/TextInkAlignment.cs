using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;

namespace ViMarkdown;

// Use the outlines at their actual shared baseline, rather than the unused
// space in font line metrics. Preserve intentional leading whitespace.
internal static class TextInkAlignment
{
    public static Vector Inset(TextLine line, string text)
    {
        Rect? ink = null;
        var x = line.Start;
        foreach (var run in line.TextRuns)
        {
            if (run is ShapedTextRun shaped && !shaped.Text.Span.Trim().IsEmpty)
            {
                var bounds = shaped.GlyphRun.BuildGeometry().Bounds;
                // Color/bitmap fonts (including emoji) have no vector outline,
                // and their font metrics can include transparent bitmap edges.
                if (bounds.Width <= 0 || bounds.Height <= 0) bounds = BitmapInkBounds(shaped.GlyphRun);
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    bounds = bounds.Translate(new Vector(x, line.Baseline - shaped.Baseline));
                    ink = ink?.Union(bounds) ?? bounds;
                }
            }
            if (run is DrawableTextRun drawable) x += drawable.Size.Width;
        }
        if (ink is not { } boundsOfInk) return default;
        var leading = 0;
        var end = Math.Min(text.Length, line.FirstTextSourceIndex + line.Length);
        for (var i = line.FirstTextSourceIndex; i < end && text[i] is not ('\r' or '\n') && char.IsWhiteSpace(text[i]); i++)
            leading++;
        var indent = line.GetDistanceFromCharacterHit(new CharacterHit(line.FirstTextSourceIndex + leading)) - line.Start;
        return new Vector(boundsOfInk.Left - indent, boundsOfInk.Top);
    }

    private static Rect BitmapInkBounds(GlyphRun run)
    {
        const int scale = 4, padding = 8;
        var size = new PixelSize((int)Math.Ceiling((run.Bounds.Width + padding * 2) * scale),
            (int)Math.Ceiling((run.Bounds.Height + padding * 2) * scale));
        using var target = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        using (var context = target.CreateDrawingContext())
        {
            context.FillRectangle(Brushes.White, new Rect(target.Size));
            using (context.PushTransform(Matrix.CreateTranslation(padding, padding)))
                context.DrawGlyphRun(Brushes.Black, run);
        }
        var stride = size.Width * 4;
        var bytes = new byte[stride * size.Height];
        var address = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            target.CopyPixels(new PixelRect(size), address, bytes.Length, stride);
            Marshal.Copy(address, bytes, 0, bytes.Length);
        }
        finally { Marshal.FreeHGlobal(address); }
        var left = size.Width; var top = size.Height; var right = -1; var bottom = -1;
        for (var y = 0; y < size.Height; y++)
            for (var px = 0; px < size.Width; px++)
            {
                var at = y * stride + px * 4;
                if (Math.Min(bytes[at], Math.Min(bytes[at + 1], bytes[at + 2])) >= 180) continue;
                left = Math.Min(left, px); top = Math.Min(top, y);
                right = Math.Max(right, px); bottom = Math.Max(bottom, y);
            }
        return right < left ? default : new Rect((double)left / scale - padding, (double)top / scale - padding,
            (double)(right - left + 1) / scale, (double)(bottom - top + 1) / scale);
    }
}
