using System.Text;

namespace ViMarkdown;

// Roll back only files created by this export, never the user's destination folder.
internal sealed class ExportOutput(string directory) : IDisposable
{
    private readonly List<string> _files = [];
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private bool _committed;
    public string DirectoryPath { get; } = directory;

    public FileStream CreateFile(string name)
    {
        var path = Path.Combine(DirectoryPath, name);
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        _files.Add(path);
        return stream;
    }

    public void WriteImage(string name, byte[] bytes)
    {
        var folder = Path.GetDirectoryName(Path.Combine(DirectoryPath, name))!;
        if (!_directories.Contains(folder))
        {
            if (Directory.Exists(folder) || File.Exists(folder))
                throw new IOException("画像出力先が既に存在します: " + folder);
            Directory.CreateDirectory(folder);
            _directories.Add(folder);
        }
        using var stream = CreateFile(name);
        stream.Write(bytes);
    }

    public void WriteText(string name, string text)
    {
        using var stream = CreateFile(name);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    public void Commit() => _committed = true;

    public void Dispose()
    {
        if (_committed) return;
        foreach (var path in _files.AsEnumerable().Reverse())
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var path in _directories)
        {
            // A nonempty directory may contain another application's new file; preserve it.
            try { Directory.Delete(path, false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
