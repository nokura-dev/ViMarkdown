using Microsoft.Data.Sqlite;

namespace ViMarkdown;

public sealed partial class WorkspaceStore : IDisposable
{
    private readonly SqliteConnection _connection;
    public string DatabasePath { get; }
    public WorkspaceStore(string path)
    {
        DatabasePath = path;
        if (path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString());
        try
        {
            _connection.Open();
            InitializeSchema();
            PurgeExpired(DateTimeOffset.UtcNow);
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }
    public void PurgeExpired(DateTimeOffset now) => Execute(
        "DELETE FROM items WHERE is_note=0 AND deleted_at IS NOT NULL AND deleted_at<=$cutoff",
        ("$cutoff", now.ToUniversalTime().Subtract(TimeSpan.FromDays(30)).Ticks));

    public string AddImage(byte[] data)
    {
        var id = Guid.NewGuid().ToString("N");
        Execute("INSERT INTO images(id,data) VALUES($id,$data)", ("$id", id), ("$data", data));
        return "asset://" + id;
    }
    public byte[]? GetImage(string url)
    {
        if (!url.StartsWith("asset://", StringComparison.Ordinal)) return null;
        using var command = CreateCommand("SELECT data FROM images WHERE id=$id", ("$id", url[8..]));
        return command.ExecuteScalar() as byte[];
    }
    // Callers own and dispose the command. Keep parameter null handling identical
    // for reads, writes, and schema migration queries.
    private SqliteCommand CreateCommand(string sql, params (string Key, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        try
        {
            command.CommandText = sql;
            foreach (var (key, value) in parameters)
                command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            return command;
        }
        catch
        {
            command.Dispose();
            throw;
        }
    }

    private static WorkspaceItem ReadItem(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1),
        reader.GetString(2), reader.GetBoolean(3), reader.GetString(4))
    {
        UpdatedAt = new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero),
        DeletedAt = reader.IsDBNull(6) ? null : new DateTimeOffset(reader.GetInt64(6), TimeSpan.Zero)
    };

    private void Execute(string sql, params (string, object?)[] parameters)
    {
        using var command = CreateCommand(sql, parameters);
        command.ExecuteNonQuery();
    }
    public List<WorkspaceItem> Items() => ReadItems(false);
    public List<WorkspaceItem> TrashItems() => ReadItems(true);
    private List<WorkspaceItem> ReadItems(bool deleted)
    {
        using var command = CreateCommand(
            "SELECT id,note_id,name,is_note,body,updated_at,deleted_at FROM items WHERE deleted_at IS " +
            (deleted ? "NOT NULL ORDER BY deleted_at DESC,id" : "NULL ORDER BY is_note DESC,name COLLATE NOCASE,id"));
        using var reader = command.ExecuteReader();
        var items = new List<WorkspaceItem>();
        while (reader.Read()) items.Add(ReadItem(reader));
        return items;
    }
    public long Create(string name, bool isNote, long? noteId, string body = "")
    {
        if (isNote && noteId.HasValue) throw new InvalidOperationException("ノートは入れ子にできません。");
        ValidateNote(noteId); ValidateName(name);
        Execute("INSERT INTO items(note_id,name,is_note,body,updated_at) VALUES($p,$n,$f,$b,$u)", ("$p", noteId), ("$n", name.Trim()), ("$f", isNote), ("$b", body), ("$u", DateTimeOffset.UtcNow.Ticks));
        using var command = CreateCommand("SELECT last_insert_rowid()");
        return (long)command.ExecuteScalar()!;
    }
    public void Save(long id, string body)
    {
        using var transaction = _connection.BeginTransaction();
        if (DatabasePath != ":memory:")
            Execute("INSERT OR IGNORE INTO edit_history_pending(page_id,baseline) SELECT id,body FROM items WHERE id=$id AND is_note=0 AND deleted_at IS NULL AND body<>$b", ("$id", id), ("$b", body));
        Execute("UPDATE items SET body=$b,updated_at=$u WHERE id=$id AND is_note=0 AND deleted_at IS NULL AND body<>$b", ("$b", body), ("$id", id), ("$u", DateTimeOffset.UtcNow.Ticks));
        transaction.Commit();
    }
    public void Rename(long id, string name) { ValidateName(name); Execute("UPDATE items SET name=$n,updated_at=$u WHERE id=$id AND deleted_at IS NULL AND name<>$n", ("$n", name.Trim()), ("$id", id), ("$u", DateTimeOffset.UtcNow.Ticks)); }
    public void Delete(long id)
        => DeleteItems([id]);

    public void DeleteItems(IEnumerable<long> ids)
    {
        using var transaction = _connection.BeginTransaction();
        foreach (var id in ids.Distinct())
        {
            Execute("UPDATE items SET deleted_at=$d WHERE is_note=0 AND deleted_at IS NULL AND (id=$id OR note_id=$id)",
                ("$d", DateTimeOffset.UtcNow.Ticks), ("$id", id));
            // Detach retained pages before the note's cascading delete.
            Execute("UPDATE items SET note_id=NULL WHERE note_id=$id AND EXISTS(SELECT 1 FROM items WHERE id=$id AND is_note=1)", ("$id", id));
            Execute("DELETE FROM items WHERE id=$id AND is_note=1", ("$id", id));
        }
        transaction.Commit();
    }
    public void Restore(long id) => Execute("UPDATE items SET deleted_at=NULL WHERE id=$id AND is_note=0 AND deleted_at IS NOT NULL", ("$id", id));
    public void PermanentlyDelete(long id) => Execute(
        "DELETE FROM items WHERE id=$id AND is_note=0 AND deleted_at IS NOT NULL", ("$id", id));
    public void MovePage(long id, long? noteId)
        => MovePages([id], noteId);

    public void MovePages(IEnumerable<long> ids, long? noteId)
    {
        ValidateNote(noteId);
        using var transaction = _connection.BeginTransaction();
        foreach (var id in ids.Distinct())
        {
            var isNote = IsNote(id) ?? throw new InvalidOperationException("対象がありません。");
            if (isNote) throw new InvalidOperationException("ノートは移動できません。");
            Execute("UPDATE items SET note_id=$n WHERE id=$id AND deleted_at IS NULL AND note_id IS NOT $n", ("$n", noteId), ("$id", id));
        }
        transaction.Commit();
    }
    private void ValidateNote(long? noteId)
    {
        if (noteId.HasValue && IsNote(noteId.Value) != true) throw new InvalidOperationException("移動先のノートがありません。");
    }
    private bool? IsNote(long id)
    {
        using var command = CreateCommand("SELECT is_note FROM items WHERE id=$id", ("$id", id));
        return command.ExecuteScalar() is long value ? value != 0 : null;
    }
    private static void ValidateName(string name) { if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("名前を入力してください。"); }
    public string? GetSetting(string key)
    {
        using var command = CreateCommand("SELECT value FROM settings WHERE key=$k", ("$k", key));
        return command.ExecuteScalar() as string;
    }
    public void SetSetting(string key, string? value) => Execute("INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$k", key), ("$v", value));
    public string Backup(string destination)
    {
        Directory.CreateDirectory(destination);
        var path = Path.Combine(destination, "ViMarkdown-backup-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            backup.Open();
            _connection.BackupDatabase(backup);
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    public void Dispose() => _connection.Dispose();
}
