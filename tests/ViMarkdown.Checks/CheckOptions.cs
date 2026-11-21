internal sealed class CheckOptions
{
    private static readonly Dictionary<string, string> RenderSuites = new()
    {
        ["--render"] = nameof(LayoutChecks),
        ["--render-highlight"] = nameof(MarkdownHighlightChecks),
        ["--render-insert"] = nameof(EditorViewChecks),
        ["--render-workspace"] = nameof(EditorViewChecks),
        ["--render-search"] = nameof(WorkspaceSearchChecks)
    };

    public bool All { get; private set; }
    public bool Affected { get; private set; }
    public bool DryRun { get; private set; }
    public bool D2 { get; private set; }
    public HashSet<string> Suites { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Renders { get; } = new(StringComparer.Ordinal);
    public IEnumerable<string> RequiredSuites => Renders.Keys.Select(key => RenderSuites[key])
        .Concat(D2 ? [nameof(D2Checks)] : []);
    public string? RenderPath(string option) => Renders.GetValueOrDefault(option);

    public static CheckOptions Parse(string[] args)
    {
        var result = new CheckOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Value()
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[i]))
                    throw new ArgumentException("オプションの値が不足しています。");
                return args[i];
            }
            switch (args[i])
            {
                case "--all": result.All = true; break;
                case "--affected": result.Affected = true; break;
                case "--dry-run": result.DryRun = true; break;
                case "--d2": result.D2 = true; break;
                case "--suite":
                    var names = Value().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (names.Length == 0) throw new ArgumentException("--suite にテスト名を指定してください。");
                    result.Suites.UnionWith(names);
                    break;
                default:
                    var option = args[i];
                    if (!RenderSuites.ContainsKey(option)) throw new ArgumentException("不明なオプション: " + option);
                    result.Renders.Add(option, Value());
                    break;
            }
        }
        if (!result.All && result.Affected && result.Suites.Count > 0)
            throw new ArgumentException("--affected と --suite は併用できません。全件実行は --all を指定してください。");
        return result;
    }
}
