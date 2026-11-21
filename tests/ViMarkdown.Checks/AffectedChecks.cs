using System.Text.Json;

internal sealed record CheckSnapshot(string Context, Dictionary<string, string> Files);
internal sealed class CheckHistory
{
    public int Version { get; set; }
    // Only a successful full run establishes/advances these two baselines.
    public string? FullContext { get; set; }
    public string[]? FullFiles { get; set; }
    public Dictionary<string, CheckSnapshot> Suites { get; set; } = new(StringComparer.Ordinal);
}
internal sealed record CheckPlan(Dictionary<string, List<string>> Reasons)
{
    public bool Includes(string suite) => Reasons.ContainsKey(suite);
}

internal static class AffectedChecks
{
    public static CheckPlan Select(IReadOnlyList<string> suites, CheckOptions options, CheckSnapshot current,
        CheckHistory? history, string? fallback = null)
    {
        var selected = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string suite, string reason)
        {
            if (!selected.TryGetValue(suite, out var reasons)) selected[suite] = reasons = [];
            if (!reasons.Contains(reason)) reasons.Add(reason);
        }
        void All(string reason) { foreach (var suite in suites) Add(suite, reason); }

        if (options.All || !options.Affected && options.Suites.Count == 0)
            All(options.All ? "--all による全件指定（自動・手動選択より優先）" : "指定なしのため全件実行");
        else if (options.Suites.Count > 0)
        {
            var unknown = options.Suites.Except(suites, StringComparer.OrdinalIgnoreCase).ToArray();
            if (unknown.Length > 0) throw new ArgumentException("不明なテスト名: " + string.Join(", ", unknown));
            foreach (var suite in suites.Where(options.Suites.Contains)) Add(suite, "--suite による手動指定");
        }
        else if (fallback is not null) All(fallback);
        else if (history is null || history.Version != 1 || history.FullFiles is null || history.FullContext is null)
            All("初回、成功した全件実行の記録なし、または記録が不正なため全件実行");
        else if (history.FullContext != current.Context) All("OS・SDK・依存パッケージ・実行条件の変更");
        else if (!history.FullFiles.ToHashSet(StringComparer.Ordinal).SetEquals(current.Files.Keys))
        {
            var added = current.Files.Keys.Except(history.FullFiles, StringComparer.Ordinal).Select(p => "+" + p);
            var deleted = history.FullFiles.Except(current.Files.Keys, StringComparer.Ordinal).Select(p => "-" + p);
            All("入力ファイルの追加・削除: " + string.Join(", ", added.Concat(deleted)));
        }
        else
        {
            foreach (var suite in suites)
            {
                if (!history.Suites.TryGetValue(suite, out var previous)) { Add(suite, "このスイートの成功記録なし"); continue; }
                if (previous.Context != current.Context) { Add(suite, "実行条件の変更"); continue; }
                var inputs = Inputs(suite, suites, current);
                foreach (var path in inputs.Keys.Union(previous.Files.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                    if (inputs.GetValueOrDefault(path) != previous.Files.GetValueOrDefault(path)) Add(suite, "変更: " + path);
            }
        }
        foreach (var required in options.RequiredSuites) Add(required, "描画または実D2検証の明示指定");
        return new(selected);
    }

    private static Dictionary<string, string> Inputs(string suite, IReadOnlyList<string> suites, CheckSnapshot snapshot) =>
        snapshot.Files.Where(file => TestImpactRules.ForFile(file.Key, suites).Contains(suite, StringComparer.Ordinal))
            .ToDictionary(file => file.Key, file => file.Value, StringComparer.Ordinal);

    public static CheckHistory RecordSuccess(IReadOnlyList<string> suites, CheckPlan plan, CheckSnapshot snapshot, CheckHistory? previous)
    {
        var result = new CheckHistory
        {
            Version = 1,
            FullContext = previous?.FullContext,
            FullFiles = previous?.FullFiles,
            Suites = previous is null ? new(StringComparer.Ordinal) : new(previous.Suites, StringComparer.Ordinal)
        };
        foreach (var suite in suites.Where(plan.Includes))
            result.Suites[suite] = new(snapshot.Context, Inputs(suite, suites, snapshot));
        if (suites.All(plan.Includes))
        {
            result.FullContext = snapshot.Context;
            result.FullFiles = snapshot.Files.Keys.Order(StringComparer.Ordinal).ToArray();
        }
        return result;
    }

    public static CheckHistory? Load(string path)
    {
        try
        {
            var state = JsonSerializer.Deserialize<CheckHistory>(File.ReadAllText(path));
            if (state is null || state.Version != 1 || state.Suites is null ||
                state.FullFiles is not null && state.FullFiles.Any(string.IsNullOrWhiteSpace) ||
                state.Suites.Any(s => s.Value is null || s.Value.Files is null || string.IsNullOrEmpty(s.Value.Context) ||
                    s.Value.Files.Any(f => string.IsNullOrEmpty(f.Key) || string.IsNullOrEmpty(f.Value)))) return null;
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }

    public static void Save(string path, CheckHistory history)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
