namespace ViMarkdown;

public sealed partial class WorkspaceStore
{
    private void InitializeSchema()
    {
        if (HasItemColumn("parent_id"))
        {
            using var migration = _connection.BeginTransaction();
            Execute("ALTER TABLE items RENAME COLUMN parent_id TO note_id; ALTER TABLE items RENAME COLUMN is_folder TO is_note; UPDATE items SET note_id=NULL WHERE is_note=1; INSERT INTO settings(key,value) SELECT 'note',value FROM settings WHERE key='folder' ON CONFLICT(key) DO NOTHING; DELETE FROM settings WHERE key='folder';");
            migration.Commit();
        }

        Execute("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS items(id INTEGER PRIMARY KEY, note_id INTEGER REFERENCES items(id) ON DELETE CASCADE, name TEXT NOT NULL, is_note INTEGER NOT NULL, body TEXT NOT NULL DEFAULT ''); CREATE TABLE IF NOT EXISTS images(id TEXT PRIMARY KEY, data BLOB NOT NULL); CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT);");
        Execute("CREATE TABLE IF NOT EXISTS edit_history_pending(page_id INTEGER PRIMARY KEY REFERENCES items(id) ON DELETE CASCADE, baseline TEXT NOT NULL)");
        if (!HasItemColumn("updated_at"))
            Execute("ALTER TABLE items ADD COLUMN updated_at INTEGER NOT NULL DEFAULT 0");
        if (!HasItemColumn("deleted_at"))
            Execute("ALTER TABLE items ADD COLUMN deleted_at INTEGER");
    }

    private bool HasItemColumn(string name)
    {
        using var command = CreateCommand(
            "SELECT COUNT(*) FROM pragma_table_info('items') WHERE name=$name", ("$name", name));
        return (long)command.ExecuteScalar()! > 0;
    }
}
