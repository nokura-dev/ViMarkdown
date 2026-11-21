using System.Buffers.Binary;
using Avalonia;
using Avalonia.Media.Imaging;

namespace ViMarkdown;

// Skia decodes encoded images at 96 DPI. Keep PNG density ourselves so saving,
// DB reloads and previews do not turn high-density pixels into logical pixels.
public static class ImageContent
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static Vector? ReadPngDpi(ReadOnlySpan<byte> png)
    {
        if (!png.StartsWith(Signature)) return null;
        for (var offset = 8; offset <= png.Length - 12;)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
            if (length > png.Length - offset - 12) return null;
            if (length == 9 && png.Slice(offset + 4, 4).SequenceEqual("pHYs"u8) && png[offset + 16] == 1)
            {
                var x = BinaryPrimitives.ReadUInt32BigEndian(png[(offset + 8)..]) * .0254;
                var y = BinaryPrimitives.ReadUInt32BigEndian(png[(offset + 12)..]) * .0254;
                if (double.IsFinite(x) && x > 0 && double.IsFinite(y) && y > 0) return new(x, y);
                return null;
            }
            offset += (int)length + 12;
        }
        return null;
    }

    public static Vector? ReadTiffDpi(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8) return null;
        var little = tiff[..2].SequenceEqual("II"u8);
        if (!little && !tiff[..2].SequenceEqual("MM"u8)) return null;
        static ushort U16(ReadOnlySpan<byte> b, bool le) => le ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);
        static uint U32(ReadOnlySpan<byte> b, bool le) => le ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b);
        if (U16(tiff[2..], little) != 42) return null;
        var directory = U32(tiff[4..], little);
        if (directory > tiff.Length - 2) return null;
        var offset = (int)directory;
        var count = U16(tiff[offset..], little);
        if (count > (tiff.Length - offset - 2) / 12) return null;
        double? x = null, y = null;
        ushort unit = 2; // TIFF default: inches.
        for (var index = 0; index < count; index++)
        {
            var entry = tiff.Slice(offset + 2 + index * 12, 12);
            var tag = U16(entry, little);
            var type = U16(entry[2..], little);
            if (U32(entry[4..], little) != 1) continue;
            if (tag == 296 && type == 3) unit = U16(entry[8..], little);
            if ((tag == 282 || tag == 283) && type == 5)
            {
                var valueOffset = U32(entry[8..], little);
                if (valueOffset > tiff.Length - 8) return null;
                var rational = tiff.Slice((int)valueOffset, 8);
                var denominator = U32(rational[4..], little);
                if (denominator == 0) return null;
                var value = (double)U32(rational, little) / denominator;
                if (tag == 282) x = value; else y = value;
            }
        }
        if (x is not > 0 || y is not > 0 || unit is not (2 or 3)) return null;
        var factor = unit == 3 ? 2.54 : 1;
        return new(x.Value * factor, y.Value * factor);
    }

    // AppKit measures image size in 72 points/inch, whereas our saved PNG
    // density uses 96 logical units/inch. A 144-DPI Retina capture is 2x.
    public static Vector? ClipboardDpi(byte[]? encoded, bool macOS)
    {
        if (encoded is null) return null;
        var dpi = ReadPngDpi(encoded) ?? ReadTiffDpi(encoded);
        return dpi is { } value && macOS ? value * (96.0 / 72) : dpi;
    }

    public static byte[] EncodePng(Bitmap bitmap, Vector? density = null)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        var png = stream.ToArray();
        var dpi = density ?? bitmap.Dpi;
        if (dpi == new Vector(96, 96)) return png;
        // Insert density after IHDR. Bitmap.Save emits no density chunk.
        var chunk = new byte[21];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, 9);
        "pHYs"u8.CopyTo(chunk.AsSpan(4));
        static uint Density(double dpi) => (uint)Math.Clamp(Math.Round(dpi / .0254), 1, uint.MaxValue);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8), Density(dpi.X));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(12), Density(dpi.Y));
        chunk[16] = 1;
        uint crc = uint.MaxValue;
        foreach (var value in chunk.AsSpan(4, 13))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(17), ~crc);
        return [.. png.AsSpan(0, 33), .. chunk, .. png.AsSpan(33)];
    }

    public static Size DisplaySize(Bitmap bitmap, byte[] data, double width)
    {
        var dpi = ReadPngDpi(data) ?? bitmap.Dpi;
        // One scale on both axes preserves the pixel aspect ratio even for
        // files with inconsistent X/Y density. Never upscale low-DPI images.
        var naturalScale = 96 / Math.Max(96, Math.Max(dpi.X, dpi.Y));
        var scale = Math.Min(naturalScale, Math.Min(Math.Max(1, width) / bitmap.PixelSize.Width, 336.0 / bitmap.PixelSize.Height));
        return new(bitmap.PixelSize.Width * scale, bitmap.PixelSize.Height * scale);
    }
}
