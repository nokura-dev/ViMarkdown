using Avalonia.Media;

namespace ViMarkdown;

public record DisplayUnit(string Text, int Start, int End, double Size = Typography.Body,
    bool Bold = false, bool Italic = false, bool Code = false,
    bool Strike = false, string? LinkUrl = null, int? TaskOffset = null, bool Rule = false,
    IReadOnlyList<TextAlignment>? Alignments = null, double HeadingRuleThickness = 0, double BottomSpacing = 0,
    string? ImageUrl = null, string? D2Code = null, IReadOnlyList<IReadOnlyList<IReadOnlyList<DisplayUnit>>>? Table = null);
