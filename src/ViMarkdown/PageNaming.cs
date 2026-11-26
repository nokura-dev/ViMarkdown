using System.Globalization;

namespace ViMarkdown;

internal static class PageNaming
{
    public const string TimestampFormat = "yyyy/MM/dd HH:mm";

    public static string AutomaticName(string body)
    {
        var lines = body.Split('\n');
        var first = CleanLine(lines[0]);
        var dated = DateTime.TryParseExact(first, TimestampFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        var name = CleanLine(dated ? lines.ElementAtOrDefault(1) ?? "" : lines[0]);
        return dated && string.IsNullOrWhiteSpace(name) ? first : name;
    }

    public static string WindowTitle(string name) => "ViMarkdown — " + name;

    private static string CleanLine(string line) => line.Trim().TrimStart('#').Trim();
}
