namespace ViMarkdown;

public enum PageSortOrder { Name, UpdatedNewest, UpdatedOldest }

public static class WorkspaceOrdering
{
    public static IOrderedEnumerable<WorkspaceItem> ByName(IEnumerable<WorkspaceItem> items) =>
        items.OrderBy(i => i.Name, NaturalNameComparer.Instance).ThenBy(i => i.Id);

    public static IEnumerable<WorkspaceItem> Pages(IEnumerable<WorkspaceItem> items, PageSortOrder order) => order switch
    {
        PageSortOrder.UpdatedNewest => items.OrderByDescending(i => i.UpdatedAt).ThenBy(i => i.Name, NaturalNameComparer.Instance).ThenBy(i => i.Id),
        PageSortOrder.UpdatedOldest => items.OrderBy(i => i.UpdatedAt).ThenBy(i => i.Name, NaturalNameComparer.Instance).ThenBy(i => i.Id),
        _ => ByName(items)
    };
}

public sealed class NaturalNameComparer : IComparer<string>
{
    public static NaturalNameComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return x is null ? (y is null ? 0 : -1) : 1;
        var a = 0; var b = 0;
        while (a < x.Length && b < y.Length)
        {
            if (char.IsAsciiDigit(x[a]) && char.IsAsciiDigit(y[b]))
            {
                var endA = a; var endB = b;
                while (endA < x.Length && char.IsAsciiDigit(x[endA])) endA++;
                while (endB < y.Length && char.IsAsciiDigit(y[endB])) endB++;
                while (a < endA && x[a] == '0') a++;
                while (b < endB && y[b] == '0') b++;
                var result = (endA - a).CompareTo(endB - b);
                if (result == 0) result = x.AsSpan(a, endA - a).CompareTo(y.AsSpan(b, endB - b), StringComparison.Ordinal);
                if (result != 0) return result;
                a = endA; b = endB;
            }
            else
            {
                var result = char.ToUpperInvariant(x[a]).CompareTo(char.ToUpperInvariant(y[b]));
                if (result != 0) return result;
                a++; b++;
            }
        }
        return (x.Length - a).CompareTo(y.Length - b);
    }
}
