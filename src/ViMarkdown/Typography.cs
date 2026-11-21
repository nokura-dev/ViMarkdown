using Avalonia.Media;

namespace ViMarkdown;

public static class Typography
{
    public static readonly FontFamily BodyFont = new("avares://ViMarkdown/Assets/Fonts/MigMix1M#MigMix 1M");
    public static readonly FontFamily EditorFont = BodyFont;
    public static readonly Typeface EditorTypeface = new(EditorFont);
    public static readonly FontFamily JapaneseUiFont = BodyFont;
    public static readonly Typeface BodyTypeface = new(BodyFont);
    public const double Scale = 0.8;
    public const double RichScale = 0.8;
    public const double InsertBody = 17 * Scale;
    public const double LineNumber = 10;
    public const double Ui = 13;
    public const double Body = 17 * RichScale;
    public const double HeadingSpacing = 6;
    public const double Checkbox = Body * 1.75;
    public const double Code = 16 * RichScale;
    public const double Help = 10 * Scale;
    public const double Diagnostic = 15 * RichScale;
}
