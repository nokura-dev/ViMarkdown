using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia;
using Markdig.Syntax;

namespace ViMarkdown;

public sealed record D2Result(byte[]? Png, string? Error)
{
    public bool Success => Png is not null;
}

// Bounded, process-local cache shared by preview and export. Failed renders are
// cached as well; restart after installing/changing the renderer configuration.
public sealed class D2Renderer
{
    public static D2Renderer Shared { get; } = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, Task<D2Result>> _cache = [];
    private readonly Queue<string> _order = new();
    private readonly SemaphoreSlim _worker = new(1);
    private readonly Func<string, Task<D2Result>> _render;
    private readonly bool _customRender;
    public D2Renderer(Func<string, Task<D2Result>>? render = null)
    { _customRender = render is not null; _render = render ?? (_ => throw new InvalidOperationException()); }

    public static bool IsD2(FencedCodeBlock block)
        => string.Equals(block.Info, "d2", StringComparison.OrdinalIgnoreCase);
    public static string Code(FencedCodeBlock block) => block.Lines.ToString();

    public Task<D2Result> RenderAsync(string code)
    {
        if (code.Length > 100_000) return Task.FromResult(new D2Result(null, "D2: コードは100,000文字以内にしてください。"));
        D2Input input;
        try { input = D2Policy.Prepare(code); }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or ArgumentException)
        { return Task.FromResult(new D2Result(null, "D2: " + ex.Message)); }
        var key = input.Key;
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
            // Do not evict running tasks: repeated preview requests must not
            // enqueue duplicates while a large document is being rendered.
            while (_order.Count >= 64)
            {
                var oldest = _order.Peek();
                if (!_cache[oldest].IsCompleted)
                    return Task.FromResult(new D2Result(null, "D2: 描画待ちが多すぎます。ページを開き直してください。"));
                _order.Dequeue(); _cache.Remove(oldest);
            }
            var task = Task.Run(async () =>
            {
                await _worker.WaitAsync();
                try { return await (_customRender ? _render(code) : RenderCli(input)); }
                catch (Exception ex) { return new D2Result(null, "D2描画失敗: " + ex.Message); }
                finally { _worker.Release(); }
            });
            _cache.Add(key, task); _order.Enqueue(key);
            return task;
        }
    }

    private static async Task<D2Result> RenderCli(D2Input input)
    {
        var cli = RuntimeAssets.D2Executable;
        if (!File.Exists(cli)) return new(null, "D2が同梱されていません。開発環境ではscripts/prepare-d2.ps1で対象OS用CLIを準備してください。");
        var directory = Path.Combine(Path.GetTempPath(), "vimarkdown-d2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var image in input.Images)
            {
                using var bitmap = new Bitmap(new MemoryStream(image.Bytes));
                if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 16_000_000)
                    return new(null, "D2: 参照画像は1,600万画素以内にしてください。");
                // Re-encode to owned PNG; D2 never opens the original path.
                bitmap.Save(Path.Combine(directory, image.Name));
            }
            var source = Path.Combine(directory, "input.d2");
            var output = Path.Combine(directory, "output.png");
            await File.WriteAllTextAsync(source, input.Code, new UTF8Encoding(false));
            var start = new ProcessStartInfo(cli)
            {
                UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
                CreateNoWindow = true, WorkingDirectory = directory
            };
            // Do not discover external plugins or inherit D2_* rendering settings.
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            start.Environment.Clear();
            start.Environment["PATH"] = Path.GetDirectoryName(cli)!;
            start.Environment["DEBUG"] = "false";
            if (OperatingSystem.IsWindows() && systemRoot is not null) start.Environment["SystemRoot"] = systemRoot;
            start.Environment["TEMP"] = directory;
            start.Environment["TMP"] = directory;
            start.Environment["TMPDIR"] = directory;
            foreach (var arg in new[] { "--layout=dagre", "--theme=1", "--target=", "--scale=0.5", "--pad=24", "--timeout=40" })
                start.ArgumentList.Add(arg);
            foreach (var font in new[] { "regular", "italic", "bold", "semibold", "mono", "mono-italic", "mono-bold", "mono-semibold" })
            {
                var name = font.Contains("bold", StringComparison.Ordinal) ? "migmix-1m-bold.ttf" : "migmix-1m-regular.ttf";
                start.ArgumentList.Add("--font-" + font);
                start.ArgumentList.Add(Path.Combine(RuntimeAssets.FontDirectory, name));
            }
            start.ArgumentList.Add(source); start.ArgumentList.Add(output);
            using var process = Process.Start(start) ?? throw new IOException("D2プロセスを開始できません。");
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                await Task.WhenAll(stderr, stdout);
                return new(null, "D2: 描画が45秒でタイムアウトしました。");
            }
            var error = await stderr; await stdout;
            if (process.ExitCode != 0 || !File.Exists(output))
            {
                var lines = error.Replace(directory, "").Replace("\r", "").Split('\n')
                    .Where(line => !line.StartsWith("info:", StringComparison.Ordinal)).Take(12);
                var diagnostic = string.Join("\n", lines).Trim();
                if (diagnostic.Length == 0) diagnostic = $"CLI終了コード {process.ExitCode}";
                return new(null, "D2構文・描画エラー: " + diagnostic[..Math.Min(1200, diagnostic.Length)]);
            }
            if (new FileInfo(output).Length > 8 * 1024 * 1024) return new(null, "D2: 生成画像が8MBを超えています。");
            var bytes = await File.ReadAllBytesAsync(output);
            using var rendered = new Bitmap(new MemoryStream(bytes));
            if ((long)rendered.PixelSize.Width * rendered.PixelSize.Height > 16_000_000)
                return new(null, "D2: 生成画像が1,600万画素を超えています。");
            return new(bytes, null);
        }
        finally { Directory.Delete(directory, true); }
    }
}
