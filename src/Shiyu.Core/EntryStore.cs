using Microsoft.Data.Sqlite;

namespace Shiyu.Core;

/// <summary>
/// The clipboard history, stored in SQLite. Deliberately concrete rather than
/// behind a port: search, filtering and retention are exactly the logic a fake
/// store would stop testing.
/// </summary>
public sealed class EntryStore : IDisposable
{
    private readonly SqliteConnection _connection;

    private EntryStore(SqliteConnection connection) => _connection = connection;

    public static EntryStore Open(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        connection.Open();

        var store = new EntryStore(connection);
        store.CreateSchema();
        return store;
    }

    private void CreateSchema()
    {
        // WAL keeps readers from blocking the writer and leaves the database
        // consistent if the process is killed — the history must survive that.
        Execute("PRAGMA journal_mode = WAL;");
        Execute("""
            CREATE TABLE IF NOT EXISTS entries (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                text        TEXT    NOT NULL,
                source_app  TEXT    NULL,
                created_at  INTEGER NOT NULL
            );
            """);
        Execute("CREATE INDEX IF NOT EXISTS idx_entries_created_at ON entries (created_at DESC);");
    }

    public Entry Append(string text, string? sourceApp, DateTimeOffset createdAt)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO entries (text, source_app, created_at)
            VALUES ($text, $sourceApp, $createdAt)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$sourceApp", (object?)sourceApp ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToUnixTimeMilliseconds());

        var id = (long)command.ExecuteScalar()!;
        return new Entry(id, text, sourceApp, createdAt);
    }

    /// <summary>The newest entry, or null when the history is empty.</summary>
    public Entry? MostRecent() => Recent(limit: 1).FirstOrDefault();

    /// <summary>
    /// Moves an existing entry to the top of the history without creating a
    /// second copy of it.
    /// </summary>
    public void Touch(long id, DateTimeOffset at)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE entries SET created_at = $createdAt WHERE id = $id;";
        command.Parameters.AddWithValue("$createdAt", at.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Entry> Recent(int limit)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, text, source_app, created_at
            FROM entries
            ORDER BY created_at DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var entries = new List<Entry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new Entry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3))));
        }

        return entries;
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
