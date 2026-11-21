using System.Runtime.InteropServices;
using System.Buffers.Binary;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ViMarkdown;

internal static class ImageSizingChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var (pixels, dpi, width, expected) in new[]
        {
            (new PixelSize(400, 200), new Vector(192, 192), 800.0, new Size(200, 100)),
            (new PixelSize(80, 40), new Vector(96, 96), 800.0, new Size(80, 40)),
            (new PixelSize(500, 1000), new Vector(96, 96), 800.0, new Size(168, 336)),
            (new PixelSize(800, 200), new Vector(96, 96), 137.0, new Size(137, 34.25)),
            (new PixelSize(200, 100), new Vector(192, 96), 800.0, new Size(100, 50))
        })
        {
            using var original = new RenderTargetBitmap(pixels, dpi);
            using (var drawing = original.CreateDrawingContext())
                drawing.FillRectangle(Brushes.Red, new Rect(original.Size));
            var png = ImageContent.EncodePng(original);
            using var decoded = new Bitmap(new MemoryStream(png));
            check(decoded.PixelSize == pixels, $"画像の元ピクセルを保持 {pixels} / {dpi}");
            var actual = ImageContent.DisplaySize(decoded, png, width);
            check(Math.Abs(actual.Width - expected.Width) < .03 && Math.Abs(actual.Height - expected.Height) < .03,
                $"DPIと表示上限を反映 {pixels} / {dpi}: {actual}");
            var bounds = DrawnBounds(png, width);
            var renderedSize = ImageContent.DisplaySize(decoded, png, Math.Max(1, width - 4));
            check(bounds.X == 4, "画像の左側に4pxのカーソル用余白を確保");
            check(Math.Abs(bounds.Width - renderedSize.Width) <= 1 && Math.Abs(bounds.Height - renderedSize.Height) <= 1,
                $"描画ピクセルの寸法と縦横比 {pixels} / {dpi}: {bounds.Size}");
            using var store = new WorkspaceStore(":memory:");
            var url = store.AddImage(png);
            using var restored = new Bitmap(new MemoryStream(store.GetImage(url)!));
            check(ImageContent.DisplaySize(restored, store.GetImage(url)!, width) == actual, "DB保存後も表示サイズを保持");
        }
        foreach (var little in new[] { true, false })
        {
            var tiff = TiffDensity(little);
            check(ImageContent.ReadTiffDpi(tiff) == new Vector(144, 144), $"TIFF解像度を読み取り endian={little}");
            var dpi = ImageContent.ClipboardDpi(tiff, true);
            check(dpi == new Vector(192, 192), "Macの144 DPIを2倍密度として保存");
            using var screenshot = new RenderTargetBitmap(new PixelSize(738, 472));
            using (var drawing = screenshot.CreateDrawingContext()) drawing.FillRectangle(Brushes.Red, new Rect(screenshot.Size));
            var png = ImageContent.EncodePng(screenshot, dpi);
            var bounds = DrawnBounds(png, 800);
            check(bounds.Size == new Size(369, 236), "Monosnapの738×472画像を元の369×236で描画");
            check(ImageContent.ClipboardDpi(tiff, false) == new Vector(144, 144), "Mac以外にはポイント換算を適用しない");
            tiff[54] = 0; tiff[55] = 0; tiff[56] = 0; tiff[57] = 0;
            check(ImageContent.ReadTiffDpi(tiff) is null, "TIFFのゼロ除算を拒否");
        }
        check(ImageContent.ReadTiffDpi([73, 73, 42, 0, 255, 255, 255, 255]) is null, "TIFFの不正なディレクトリ位置を拒否");
        check(ImageContent.ReadPngDpi([1, 2, 3]) is null, "PNG以外の密度情報は無視");
        check(ImageContent.ReadPngDpi([137, 80, 78, 71, 13, 10, 26, 10, 255, 255, 255, 255, 112, 72, 89, 115, 0, 0, 0, 0]) is null,
            "不正なPNGチャンク長を安全に処理");
    }

    private static byte[] TiffDensity(bool little)
    {
        var bytes = new byte[66];
        bytes[0] = bytes[1] = (byte)(little ? 'I' : 'M');
        void U16(int at, ushort value)
        {
            if (little) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
            else BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), value);
        }
        void U32(int at, uint value)
        {
            if (little) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
            else BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), value);
        }
        U16(2, 42); U32(4, 8); U16(8, 3);
        U16(10, 282); U16(12, 5); U32(14, 1); U32(18, 50);
        U16(22, 283); U16(24, 5); U32(26, 1); U32(30, 58);
        U16(34, 296); U16(36, 3); U32(38, 1); U16(42, 2);
        U32(50, 144); U32(54, 1); U32(58, 144); U32(62, 1);
        return bytes;
    }

    private static Rect DrawnBounds(byte[] png, double width)
    {
        using var layout = new RichTextLayout(Projection.Build("![画像](asset://test)"), width, _ => png);
        using var target = new RenderTargetBitmap(new PixelSize(900, 500), new Vector(96, 96));
        using (var drawing = target.CreateDrawingContext()) layout.Draw(drawing);
        using var stream = new MemoryStream();
        target.Save(stream); stream.Position = 0;
        using var bitmap = new Bitmap(stream);
        var stride = bitmap.PixelSize.Width * 4;
        var bytes = new byte[stride * bitmap.PixelSize.Height];
        var address = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), address, bytes.Length, stride);
            Marshal.Copy(address, bytes, 0, bytes.Length);
        }
        finally { Marshal.FreeHGlobal(address); }
        var minX = 900; var maxX = -1; var minY = 500; var maxY = -1;
        for (var y = 0; y < 500; y++)
            for (var x = 0; x < 900; x++)
                if (bytes[y * stride + x * 4 + 3] > 127)
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
