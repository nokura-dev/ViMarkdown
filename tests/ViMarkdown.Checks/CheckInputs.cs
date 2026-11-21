using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

internal static class CheckInputs
{
    public static string FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "src", "ViMarkdown", "ViMarkdown.csproj")) &&
                    File.Exists(Path.Combine(directory.FullName, "tests", "ViMarkdown.Checks", "ViMarkdown.Checks.csproj")))
                    return directory.FullName;
        throw new InvalidOperationException("ソースのルートが見つかりません。作業フォルダから実行してください。");
    }

    public static CheckSnapshot Capture(string root, bool mermaid)
    {
        var files = Paths(root).Order(StringComparer.Ordinal).ToDictionary(path =>
            Path.GetRelativePath(root, path).Replace('\\', '/'), path => Hash(File.ReadAllBytes(path)), StringComparer.Ordinal);
        var assembly = typeof(CheckInputs).Assembly;
        var dependencies = Path.Combine(AppContext.BaseDirectory, "ViMarkdown.Checks.deps.json");
        var context = string.Join("\n", RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture,
            RuntimeInformation.ProcessArchitecture, RuntimeInformation.FrameworkDescription,
            assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
            SdkVersion(root), File.Exists(dependencies) ? Hash(File.ReadAllBytes(dependencies)) : "no-deps",
            "mermaid=" + mermaid,
            Environment.GetEnvironmentVariable("VIMARKDOWN_NODE"),
            Environment.GetEnvironmentVariable("VIMARKDOWN_MERMAID_CLI"),
            Environment.GetEnvironmentVariable("VIMARKDOWN_CHROME"));
        return new(Hash(Encoding.UTF8.GetBytes(context)), files);
    }

    public static bool Same(CheckSnapshot before, CheckSnapshot after) => before.Context == after.Context &&
        before.Files.Count == after.Files.Count && before.Files.All(f => after.Files.GetValueOrDefault(f.Key) == f.Value);

    // Refuse to certify newer source with an old executable invoked via --no-build.
    public static bool MatchesBuild(string root, CheckSnapshot snapshot)
    {
        var manifest = Path.Combine(AppContext.BaseDirectory, "check-build-inputs.txt");
        if (!File.Exists(manifest)) return false;
        var built = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(manifest))
        {
            var separator = line.LastIndexOf('|');
            if (separator < 1) return false;
            var path = Path.GetRelativePath(root, line[..separator]).Replace('\\', '/');
            if (!IsBuildInput(path)) continue;
            built[path] = line[(separator + 1)..].Trim();
        }
        // Build inputs are C# source, project/build settings, and the copied Markdown fixture.
        var expected = snapshot.Files.Where(f => IsBuildInput(f.Key)).ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        return expected.Count == built.Count && expected.All(f => built.GetValueOrDefault(f.Key) == f.Value);
    }

    internal static bool IsBuildInput(string path) => path.StartsWith("src/", StringComparison.Ordinal) ||
        path.StartsWith("tests/", StringComparison.Ordinal) || path == "docs/markdown-features-sample.md" ||
        !path.Contains('/') && IsRootConfiguration(path);

    private static IEnumerable<string> Paths(string root)
    {
        foreach (var directory in new[] { "src", "tests", "scripts", ".github" })
            foreach (var path in Walk(Path.Combine(root, directory))) yield return path;
        var tools = Path.Combine(root, "tools", "mermaid");
        if (Directory.Exists(tools))
            foreach (var path in Directory.EnumerateFiles(tools, "package*.json")) yield return path;
        var fixture = Path.Combine(root, "docs", "markdown-features-sample.md");
        if (File.Exists(fixture)) yield return fixture;
        foreach (var path in Directory.EnumerateFiles(root))
            if (IsRootConfiguration(Path.GetFileName(path))) yield return path;
    }

    private static IEnumerable<string> Walk(string directory)
    {
        if (!Directory.Exists(directory)) yield break;
        foreach (var path in Directory.EnumerateFiles(directory))
            if (Path.GetFileName(path) != ".DS_Store") yield return path;
        foreach (var child in Directory.EnumerateDirectories(directory))
            if (Path.GetFileName(child) is not ("bin" or "obj" or "node_modules" or ".git" or ".cache") &&
                !File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                foreach (var path in Walk(child)) yield return path;
    }

    private static bool IsRootConfiguration(string name) => name == ".editorconfig" ||
        name.StartsWith("Directory.", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(name).ToLowerInvariant() is ".json" or ".config" or ".props" or ".targets" or ".sln" or ".slnx" or ".csproj";

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string SdkVersion(string root)
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", "--version")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException(".NET SDK情報を取得できません。");
        if (!process.WaitForExit(10000)) { process.Kill(entireProcessTree: true); throw new IOException("SDK情報の取得がタイムアウトしました。"); }
        var version = process.StandardOutput.ReadToEnd().Trim();
        if (process.ExitCode != 0 || version.Length == 0) throw new IOException(".NET SDK情報を取得できません。");
        return version;
    }
}
