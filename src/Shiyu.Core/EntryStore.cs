using Microsoft.Data.Sqlite;

namespace Shiyu.Core;

/// <summary>
/// The clipboard history, stored in SQLite. Deliberately concrete rather than
/// behind a port: search, filtering and retention are exactly the logic a fake
/// store would stop testing.
/// </summary>
public sealed partial class EntryStore : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Internal for tests: writing rows the way an older build would have.</summary>
    internal SqliteConnection Connection => _connection;

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

    /// <summary>
    /// The shape the code expects. Bumped whenever a migration is added below.
    /// </summary>
    private const int SchemaVersion = 7;

    /// <summary>
    /// Joins tag names into one column. A unit separator, because it cannot
    /// occur in a tag the user typed — a comma very much can.
    /// </summary>
    private const char TagSeparator = '';

    private void CreateSchema()
    {
        // WAL keeps readers from blocking the writer and leaves the database
        // consistent if the process is killed — the history must survive that.
        Execute("PRAGMA journal_mode = WAL;");

        // Off by default in SQLite, and without it the cascade that removes an
        // entry's tags when the entry goes would silently not happen.
        Execute("PRAGMA foreign_keys = ON;");
        Execute("""
            CREATE TABLE IF NOT EXISTS entries (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                text        TEXT    NOT NULL,
                source_app  TEXT    NULL,
                created_at  INTEGER NOT NULL
            );
            """);
        Execute("CREATE INDEX IF NOT EXISTS idx_entries_created_at ON entries (created_at DESC);");

        Migrate();
    }

    /// <summary>
    /// Brings an older database up to date in place.
    ///
    /// The history is the point of this application, so migrations only ever
    /// add: a user who upgrades must find everything they had, not an empty
    /// list and no explanation.
    /// </summary>
    private void Migrate()
    {
        var from = ReadSchemaVersion();

        if (from < 2)
        {
            // Text entries predate images, so they default to kind 0 and carry
            // no thumbnail or original — exactly what an existing row means.
            Execute("ALTER TABLE entries ADD COLUMN kind INTEGER NOT NULL DEFAULT 0;");
            Execute("ALTER TABLE entries ADD COLUMN thumbnail BLOB NULL;");
            Execute("ALTER TABLE entries ADD COLUMN original_path TEXT NULL;");
        }

        if (from < 3)
        {
            Execute("ALTER TABLE entries ADD COLUMN pinned INTEGER NOT NULL DEFAULT 0;");

            // Tags are their own rows rather than a comma-separated column, so
            // renaming one is a single update and filtering by one is an index
            // lookup instead of a scan with string matching.
            Execute("""
                CREATE TABLE IF NOT EXISTS tags (
                    id   INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL COLLATE NOCASE UNIQUE
                );
                """);
            Execute("""
                CREATE TABLE IF NOT EXISTS entry_tags (
                    entry_id INTEGER NOT NULL REFERENCES entries(id) ON DELETE CASCADE,
                    tag_id   INTEGER NOT NULL REFERENCES tags(id)    ON DELETE CASCADE,
                    PRIMARY KEY (entry_id, tag_id)
                );
                """);
            Execute("CREATE INDEX IF NOT EXISTS idx_entry_tags_tag ON entry_tags (tag_id);");
        }

        if (from < 4)
        {
            // One row per source application: its icon, or a tombstone saying
            // none was found. A thousand entries share one cached copy, and an
            // uninstalled application keeps showing what was extracted while
            // it was still alive.
            Execute("""
                CREATE TABLE IF NOT EXISTS applications (
                    name TEXT PRIMARY KEY,
                    icon BLOB NULL
                );
                """);
        }

        if (from < 5)
        {
            // What a text entry is — link, email, colour, path — recorded at
            // write time so lists and filters never re-derive it per row.
            Execute("ALTER TABLE entries ADD COLUMN sub_type TEXT NULL;");
        }

        if (from < 6)
        {
            // The formatted forms of a rich copy, kept for pasting back with
            // formatting. Existing rows have none, and stay as they are.
            Execute("ALTER TABLE entries ADD COLUMN html TEXT NULL;");
            Execute("ALTER TABLE entries ADD COLUMN rtf TEXT NULL;");
        }

        if (from < 7)
        {
            // A file copy's paths, newline-joined and capped. Existing rows
            // have none, and stay as they are.
            Execute("ALTER TABLE entries ADD COLUMN files TEXT NULL;");
        }

        if (from != SchemaVersion)
        {
            Execute($"PRAGMA user_version = {SchemaVersion};");
        }

        BackfillSubtypes();
    }

    /// <summary>
    /// Classifies any rows recorded before subtypes existed. Idempotent and
    /// run on every open: it selects only NULL rows, so a clean database
    /// costs one indexed query and the work happens exactly once per row.
    /// </summary>
    private void BackfillSubtypes()
    {
        var updates = new List<(long id, string subtype)>();

        // The reader is fully closed before any write: SQLite allows one
        // statement at a time per connection, and a cursor held open across
        // the update turns a routine backfill into a lock error.
        using (var read = _connection.CreateCommand())
        {
            read.CommandText = "SELECT id, text FROM entries WHERE sub_type IS NULL AND kind = 0;";

            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var subtype = SubtypeClassifier.Detect(reader.GetString(1));
                if (subtype != EntrySubtype.None)
                {
                    updates.Add((reader.GetInt64(0), subtype.ToString()));
                }
            }
        }

        if (updates.Count == 0)
        {
            return;
        }

        // None-valued rows stay NULL: they are the common case, and writing
        // millions of no-ops is not free on a big history.
        using var transaction = _connection.BeginTransaction();

        using var update = _connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE entries SET sub_type = $subtype WHERE id = $id;";
        var subtypeParameter = update.CreateParameter();
        subtypeParameter.ParameterName = "$subtype";
        update.Parameters.Add(subtypeParameter);
        var idParameter = update.CreateParameter();
        idParameter.ParameterName = "$id";
        update.Parameters.Add(idParameter);

        foreach (var (id, subtype) in updates)
        {
            subtypeParameter.Value = subtype;
            idParameter.Value = id;
            update.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private int ReadSchemaVersion()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(command.ExecuteScalar());

        // A database created before versioning began still has the original
        // three columns and reports zero; treat it as version 1.
        return version == 0 && HasColumn("kind") ? 2 : Math.Max(version, 1);
    }

    private bool HasColumn(string name)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('entries') WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    public Entry Append(
        string text,
        string? sourceApp,
        DateTimeOffset createdAt,
        string? html = null,
        string? rtf = null)
    {
        var subtype = SubtypeClassifier.Detect(text);

        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO entries (text, source_app, created_at, sub_type, html, rtf)
            VALUES ($text, $sourceApp, $createdAt, $subtype, $html, $rtf)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$sourceApp", (object?)sourceApp ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$subtype",
            subtype == EntrySubtype.None ? DBNull.Value : (object)subtype.ToString());
        command.Parameters.AddWithValue("$html", (object?)html ?? DBNull.Value);
        command.Parameters.AddWithValue("$rtf", (object?)rtf ?? DBNull.Value);

        var id = (long)command.ExecuteScalar()!;
        return new Entry(id, text, sourceApp, createdAt)
        {
            Subtype = subtype,
            Html = html,
            Rtf = rtf,
        };
    }

    /// <summary>
    /// Records a file copy: the label is what the list shows, the paths are
    /// what a paste back needs, already capped by the caller.
    /// </summary>
    public Entry AppendFiles(
        IReadOnlyList<string> paths,
        string? sourceApp,
        DateTimeOffset createdAt)
    {
        var capped = paths.Count > FileEntries.Cap;
        var kept = FileEntries.WithinCap(paths, out _);
        var label = FileEntries.Label(paths, capped);

        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO entries (text, source_app, created_at, kind, files)
            VALUES ($label, $sourceApp, $createdAt, 2, $files)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$label", label);
        command.Parameters.AddWithValue("$sourceApp", (object?)sourceApp ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$files", string.Join("\n", kept));

        var id = (long)command.ExecuteScalar()!;
        return new Entry(id, label, sourceApp, createdAt)
        {
            Kind = EntryKind.Files,
            Files = kept,
        };
    }

    /// <summary>
    /// Whether the application has a row in the icon store — including a
    /// tombstone row for an icon that could not be found.
    /// </summary>
    public bool HasApplicationIcon(string name)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM applications WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    /// <summary>The cached icon's PNG bytes, or null when none was found.</summary>
    public byte[]? ApplicationIcon(string name)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT icon FROM applications WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        var result = command.ExecuteScalar();
        return result is byte[] png ? png : null;
    }

    /// <summary>
    /// Stores the icon row, ignoring the call when one already exists — see
    /// <see cref="SourceIconCache"/> for why a race must not overwrite.
    /// </summary>
    public void SaveApplicationIcon(string name, byte[]? png)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO applications (name, icon)
            VALUES ($name, $icon);
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$icon", (object?)png ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Records a copied image: the thumbnail goes in the database and stays
    /// there, the full-size original goes on disk and is subject to retention.
    /// </summary>
    public Entry AppendImage(
        string label,
        byte[] thumbnailPng,
        string originalPath,
        string? sourceApp,
        DateTimeOffset createdAt)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO entries (text, source_app, created_at, kind, thumbnail, original_path)
            VALUES ($text, $sourceApp, $createdAt, $kind, $thumbnail, $originalPath)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$text", label);
        command.Parameters.AddWithValue("$sourceApp", (object?)sourceApp ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$kind", (int)EntryKind.Image);
        command.Parameters.AddWithValue("$thumbnail", thumbnailPng);
        command.Parameters.AddWithValue("$originalPath", originalPath);

        var id = (long)command.ExecuteScalar()!;
        return new Entry(id, label, sourceApp, createdAt)
        {
            Kind = EntryKind.Image,
            ThumbnailPng = thumbnailPng,
            OriginalPath = originalPath,
        };
    }

    /// <summary>
    /// Forgets where an original used to be, once retention has deleted it.
    /// The entry and its thumbnail are untouched.
    /// </summary>
    public void ClearOriginal(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE entries SET original_path = NULL WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Every image entry that still has an original on disk.</summary>
    public IReadOnlyList<Entry> ImagesWithOriginals()
        => ImagesWhere("original_path IS NOT NULL");

    /// <summary>Image entries with originals, created within the range.</summary>
    public IReadOnlyList<Entry> ImagesCreatedBetween(DateTimeOffset from, DateTimeOffset to)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files,
                   (SELECT group_concat(t.name, char(31)) FROM tags t
                      JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
            FROM entries
            WHERE kind = $kind AND original_path IS NOT NULL
              AND created_at BETWEEN $from AND $to;
            """;
        command.Parameters.AddWithValue("$kind", (int)EntryKind.Image);
        command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());

        return ReadEntries(command);
    }

    private IReadOnlyList<Entry> ImagesWhere(string condition)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files,
                   (SELECT group_concat(t.name, char(31)) FROM tags t
                      JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
            FROM entries
            WHERE kind = $kind AND {condition};
            """;
        command.Parameters.AddWithValue("$kind", (int)EntryKind.Image);

        return ReadEntries(command);
    }

    /// <summary>Image entries whose original is older than the cutoff.</summary>
    public IReadOnlyList<Entry> ImagesWithOriginalsBefore(DateTimeOffset cutoff, int limit)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files,
                   (SELECT group_concat(t.name, char(31)) FROM tags t
                      JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
            FROM entries
            WHERE kind = $kind AND original_path IS NOT NULL AND created_at < $cutoff
            ORDER BY created_at ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$kind", (int)EntryKind.Image);
        command.Parameters.AddWithValue("$cutoff", cutoff.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$limit", limit);

        return ReadEntries(command);
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
            SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files,
                   (SELECT group_concat(t.name, char(31)) FROM tags t
                      JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
            FROM entries
            WHERE text LIKE $pattern ESCAPE '\'
            ORDER BY pinned DESC, created_at DESC, id DESC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$pattern", $"%{EscapeForLike(query)}%");
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        return ReadEntries(command);
    }

    /// <summary>
    /// Finds entries matching every part of the filter, newest first.
    ///
    /// One query rather than filtering a search in memory: combining a keyword
    /// with a date range has to narrow the whole history, not just whatever
    /// the keyword happened to return first.
    /// </summary>
    public IReadOnlyList<Entry> Find(HistoryFilter filter, int limit, int offset = 0)
    {
        var conditions = new List<string>();

        using var command = _connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            conditions.Add(@"text LIKE $pattern ESCAPE '\'");
            command.Parameters.AddWithValue("$pattern", $"%{EscapeForLike(filter.Query)}%");
        }

        if (filter.From is { } from)
        {
            conditions.Add("created_at >= $from");
            command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
        }

        if (filter.To is { } to)
        {
            conditions.Add("created_at <= $to");
            command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
        }

        if (filter.Kind is { } kind)
        {
            conditions.Add("kind = $kind");
            command.Parameters.AddWithValue("$kind", (int)kind);
        }

        if (!string.IsNullOrWhiteSpace(filter.Tag))
        {
            conditions.Add("""
                id IN (SELECT et.entry_id FROM entry_tags et
                         JOIN tags t ON t.id = et.tag_id
                        WHERE t.name = $tag)
                """);
            command.Parameters.AddWithValue("$tag", filter.Tag.Trim());
        }

        if (filter.Subtype is { } subtype)
        {
            // One filter choice, two stored values: a "path" filter means
            // both local and UNC, because to the user they are one idea.
            if (subtype == EntrySubtype.LocalPath)
            {
                conditions.Add("sub_type IN ('LocalPath', 'UncPath')");
            }
            else
            {
                conditions.Add("sub_type = $subtype");
                command.Parameters.AddWithValue("$subtype", subtype.ToString());
            }
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);

        command.CommandText = $"""
            SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files,
                   (SELECT group_concat(t.name, char(31)) FROM tags t
                      JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
            FROM entries
            {where}
            ORDER BY pinned DESC, created_at DESC, id DESC
            LIMIT $limit OFFSET $offset;
            """;
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
            SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files,
                   (SELECT group_concat(t.name, char(31)) FROM tags t
                      JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
            FROM entries
            ORDER BY pinned DESC, created_at DESC, id DESC
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
            SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files,
                   (SELECT group_concat(t.name, char(31)) FROM tags t
                      JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
            FROM entries
            ORDER BY pinned DESC, created_at DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        return ReadEntries(command);
    }

    /// <summary>A column written before an unknown value appeared — never worth a broken list over.</summary>
    private static EntrySubtype ParseSubtype(string? stored)
        => stored is not null && Enum.TryParse<EntrySubtype>(stored, out var parsed)
            ? parsed
            : EntrySubtype.None;

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
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)))
            {
                Kind = (EntryKind)reader.GetInt32(4),
                ThumbnailPng = reader.IsDBNull(5) ? null : (byte[])reader[5],
                OriginalPath = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsPinned = reader.GetInt32(7) != 0,
                Subtype = ParseSubtype(reader.IsDBNull(8) ? null : reader.GetString(8)),
                Html = reader.IsDBNull(9) ? null : reader.GetString(9),
                Rtf = reader.IsDBNull(10) ? null : reader.GetString(10),
                Files = reader.IsDBNull(11) || reader.GetString(11).Length == 0
                    ? []
                    : reader.GetString(11).Split('\n'),

                // Joined in rather than fetched per row: a list of a hundred
                // entries would otherwise be a hundred extra queries.
                Tags = reader.IsDBNull(12)
                    ? []
                    : reader.GetString(12).Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries),
            });
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
