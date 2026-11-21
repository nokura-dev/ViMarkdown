using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Markdig.Syntax;

namespace ViMarkdown;

public sealed record MermaidResult(byte[]? Png, string? Error)
{
    public bool Success => Png is not null;
}

// Bounded, process-local cache shared by preview and export. Failed renders are
// cached as well; restart after installing/changing the renderer configuration.
public sealed class MermaidRenderer
{
    public static MermaidRenderer Shared { get; } = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, Task<MermaidResult>> _cache = [];
    private readonly Queue<string> _order = new();
    private readonly SemaphoreSlim _worker = new(1);
    private readonly Func<string, Task<MermaidResult>> _render;
    public MermaidRenderer(Func<string, Task<MermaidResult>>? render = null) => _render = render ?? RenderCli;

    public static bool IsMermaid(FencedCodeBlock block)
        => string.Equals(block.Info, "mermaid", StringComparison.OrdinalIgnoreCase);
    public static string Code(FencedCodeBlock block) => block.Lines.ToString();

    public Task<MermaidResult> RenderAsync(string code)
    {
        if (code.Length > 100_000) return Task.FromResult(new MermaidResult(null, "Mermaid: コードは100,000文字以内にしてください。"));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
            // Do not evict running tasks: repeated preview requests must not
            // enqueue duplicates while a large document is being rendered.
            while (_order.Count >= 64)
            {
                var oldest = _order.Peek();
                if (!_cache[oldest].IsCompleted)
                    return Task.FromResult(new MermaidResult(null, "Mermaid: 描画待ちが多すぎます。ページを開き直してください。"));
                _order.Dequeue(); _cache.Remove(oldest);
            }
            var task = Task.Run(async () =>
            {
                await _worker.WaitAsync();
                try { return await _render(code); }
                catch (Exception ex) { return new MermaidResult(null, "Mermaid描画失敗: " + ex.Message); }
                finally { _worker.Release(); }
            });
            _cache.Add(key, task); _order.Enqueue(key);
            return task;
        }
    }

    private static string FindCli()
    {
        var configured = Environment.GetEnvironmentVariable("VIMARKDOWN_MERMAID_CLI");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        foreach (var root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, "tools", "mermaid", "node_modules", "@mermaid-js", "mermaid-cli", "src", "cli.js");
                if (File.Exists(path)) return path;
            }
        throw new FileNotFoundException("Mermaid未設定: tools/mermaidで npm ci を実行してください。");
    }

    private static async Task<MermaidResult> RenderCli(string code)
    {
        var cli = FindCli();
        var directory = Path.Combine(Path.GetTempPath(), "vimarkdown-mermaid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "input.mmd");
            var output = Path.Combine(directory, "output.png");
            var config = Path.Combine(directory, "mermaid.json");
            await File.WriteAllTextAsync(input, code);
            await File.WriteAllTextAsync(config, """{"securityLevel":"strict","maxTextSize":100000,"maxEdges":500,"flowchart":{"htmlLabels":false}}""");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("VIMARKDOWN_NODE") ?? "node")
            {
                UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true
            };
            foreach (var argument in new[] { cli, "-i", input, "-o", output, "-c", config, "-t", "default", "-b", "white", "--size", "1600", "-s", "1", "-q" })
                start.ArgumentList.Add(argument);
            var chrome = Environment.GetEnvironmentVariable("VIMARKDOWN_CHROME");
            if (string.IsNullOrWhiteSpace(chrome) && OperatingSystem.IsMacOS() && File.Exists("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"))
                chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
            if (!string.IsNullOrWhiteSpace(chrome))
            {
                var puppeteer = Path.Combine(directory, "puppeteer.json");
                await File.WriteAllTextAsync(puppeteer, JsonSerializer.Serialize(new { executablePath = chrome }));
                start.ArgumentList.Add("-p"); start.ArgumentList.Add(puppeteer);
            }
            using var process = Process.Start(start) ?? throw new IOException("Mermaidプロセスを開始できません。");
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                await Task.WhenAll(stderr, stdout);
                return new(null, "Mermaid: 描画が45秒でタイムアウトしました。");
            }
            var error = await stderr;
            await stdout;
            if (process.ExitCode != 0 || !File.Exists(output))
            {
                var syntax = error.Contains("Parse error", StringComparison.OrdinalIgnoreCase) || error.Contains("Syntax error", StringComparison.OrdinalIgnoreCase)
                    || error.Contains("UnknownDiagramError", StringComparison.Ordinal);
                var diagnostic = string.Join("\n", error.Replace("\r", "").Split('\n')
                    .TakeWhile(line => !line.TrimStart().StartsWith("at ", StringComparison.Ordinal)
                        && !line.StartsWith("Parser.", StringComparison.Ordinal)).Take(12)).Trim();
                if (diagnostic.Length == 0) diagnostic = $"CLI終了コード {process.ExitCode}";
                return new(null, (syntax ? "Mermaid構文エラー: " : "Mermaid描画失敗: ") + diagnostic[..Math.Min(1200, diagnostic.Length)]);
            }
            if (new FileInfo(output).Length > 8 * 1024 * 1024) return new(null, "Mermaid: 生成画像が8MBを超えています。");
            return new(await File.ReadAllBytesAsync(output), null);
        }
        finally { Directory.Delete(directory, true); }
    }
}
