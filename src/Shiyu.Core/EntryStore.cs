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

    /// <summary>
    /// Writes many entries in one transaction. Appending them one by one costs
    /// a commit each, which turns a bulk write into a wait measured in minutes.
    /// </summary>
    public void AppendMany(IEnumerable<NewEntry> entries)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO entries (text, source_app, created_at)
            VALUES ($text, $sourceApp, $createdAt);
            """;

        var text = command.Parameters.Add("$text", SqliteType.Text);
        var sourceApp = command.Parameters.Add("$sourceApp", SqliteType.Text);
        var createdAt = command.Parameters.Add("$createdAt", SqliteType.Integer);

        foreach (var entry in entries)
        {
            text.Value = entry.Text;
            sourceApp.Value = (object?)entry.SourceApp ?? DBNull.Value;
            createdAt.Value = entry.CreatedAt.ToUnixTimeMilliseconds();
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>The newest entry, or null when the history is empty.</summary>
    public Entry? MostRecent() => Recent(limit: 1).FirstOrDefault();

    /// <summary>
    /// Finds entries whose text contains <paramref name="query"/>, newest first.
    ///
    /// Substring matching rather than a full-text index: SQLite's full-text
    /// tokenisers do not segment Chinese, so "剪贴板历史" indexes as a single
    /// token and searching for "剪贴板" would find nothing — useless for a
    /// tool whose user writes Chinese. A scan stays well inside the time a
    /// keystroke can hide at the sizes a personal clipboard history reaches.
    /// </summary>
    public IReadOnlyList<Entry> Search(string query, int limit, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, text, source_app, created_at
            FROM entries
            WHERE text LIKE $pattern ESCAPE '\'
            ORDER BY created_at DESC, id DESC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$pattern", $"%{EscapeForLike(query)}%");
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        return ReadEntries(command);
    }

    /// <summary>
    /// A window onto the history, newest first. Every read path takes a limit:
    /// the history is never loaded into memory in one piece, however large it
    /// grows.
    /// </summary>
    public IReadOnlyList<Entry> Page(int limit, int offset)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, text, source_app, created_at
            FROM entries
            ORDER BY created_at DESC, id DESC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        return ReadEntries(command);
    }

    public int Count()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM entries;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Returns whether there was anything to delete.</summary>
    public bool Delete(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM entries WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>Deletes entries created within the range, both ends included.</summary>
    public int DeleteCreatedBetween(DateTimeOffset from, DateTimeOffset to)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            DELETE FROM entries
            WHERE created_at BETWEEN $from AND $to;
            """;
        command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
        return command.ExecuteNonQuery();
    }

    public int DeleteAll()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM entries;";
        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Neutralises the wildcards LIKE would otherwise read in a user's query —
    /// an unescaped "%" would silently match the entire history.
    /// </summary>
    private static string EscapeForLike(string query)
        => query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

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

        return ReadEntries(command);
    }

    private static IReadOnlyList<Entry> ReadEntries(SqliteCommand command)
    {
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
