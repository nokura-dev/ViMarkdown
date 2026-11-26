using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;

namespace ViMarkdown;

// The desktop renderer and D2 share the same external TTF files.
internal sealed class LocalFontCollection : FontCollectionBase
{
    public static readonly Uri CollectionKey = new("fonts:ViMarkdown");
    private readonly List<FontFamily> _families = [];
    public override Uri Key => CollectionKey;
    public override int Count => _families.Count;
    public override FontFamily this[int index] => _families[index];
    public override IEnumerator<FontFamily> GetEnumerator() => _families.GetEnumerator();

    public override void Initialize(IFontManagerImpl fontManager)
    {
        foreach (var name in new[] { "migmix-1m-regular.ttf", "migmix-1m-bold.ttf" })
        {
            using var stream = File.OpenRead(Path.Combine(RuntimeAssets.FontDirectory, name));
            if (!fontManager.TryCreateGlyphTypeface(stream, FontSimulations.None, out var face))
                throw new IOException("フォントを読み込めません: " + name);
            var cache = _glyphTypefaceCache.GetOrAdd(face.FamilyName, _ =>
            {
                _families.Add(new FontFamily(Key.AbsoluteUri + "#" + face.FamilyName));
                return new ConcurrentDictionary<FontCollectionKey, IGlyphTypeface?>();
            });
            cache[new FontCollectionKey(face.Style, face.Weight, face.Stretch)] = face;
        }
    }

    public override bool TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight,
        FontStretch stretch, [NotNullWhen(true)] out IGlyphTypeface? glyphTypeface)
    {
        glyphTypeface = null;
        if (!_glyphTypefaceCache.TryGetValue(familyName, out var cache)) return false;
        var key = new FontCollectionKey(style, weight, stretch);
        if (cache.TryGetValue(key, out glyphTypeface) && glyphTypeface is not null) return true;
        var normal = new FontCollectionKey(FontStyle.Normal,
            weight >= FontWeight.SemiBold ? FontWeight.Bold : FontWeight.Normal, FontStretch.Normal);
        if (!cache.TryGetValue(normal, out glyphTypeface) || glyphTypeface is null) return false;
        if (style != FontStyle.Normal && TryCreateSyntheticGlyphTypeface(glyphTypeface, style, weight, stretch, out var synthetic))
            glyphTypeface = synthetic;
        return true;
    }
}
