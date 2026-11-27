using System.Text.Json;

namespace ViMarkdown;

internal sealed record WorkspaceConfiguration(string DatabaseFolder)
{
    internal static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ViMarkdown");
    internal static string ConfigPath => Path.Combine(DefaultFolder, "ViMarkdown.config.json");

    internal static string DatabasePath()
    {
        if (!File.Exists(ConfigPath)) return Path.Combine(DefaultFolder, "ViMarkdown.db");
        var config = JsonSerializer.Deserialize<WorkspaceConfiguration>(File.ReadAllText(ConfigPath));
        if (config is null || string.IsNullOrWhiteSpace(config.DatabaseFolder) || !Path.IsPathFullyQualified(config.DatabaseFolder))
            throw new InvalidOperationException("データ保存場所の設定が不正です: " + ConfigPath);
        // A disconnected drive must not silently become an empty workspace.
        var path = Path.Combine(config.DatabaseFolder, "ViMarkdown.db");
        if (!File.Exists(path)) throw new FileNotFoundException("設定されたデータベースが見つかりません。保存先を確認してください。", path);
        return path;
    }

    internal static string Prepare(string folder)
    {
        Directory.CreateDirectory(DefaultFolder);
        var temporary = Path.Combine(DefaultFolder, "ViMarkdown.config." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new WorkspaceConfiguration(folder),
                new JsonSerializerOptions { WriteIndented = true }));
            return temporary;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }
}
