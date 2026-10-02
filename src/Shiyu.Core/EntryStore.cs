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

    /// <summary>
    /// Every public member runs under this gate. One connection serves the
    /// UI thread and the pool (image recording, retention, backup), and a
    /// connection is not safe to share: a command made on one thread adopts
    /// whatever transaction another thread has open, and last_insert_rowid()
    /// belongs to the connection, not the caller. Monitor is re-entrant, so
    /// members calling members is fine. Nothing public returns a lazy
    /// sequence — a reader that outlived the lock would be unguarded.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>The transaction <see cref="RunInTransaction"/> holds open, if any. Touched only under the gate.</summary>
    private SqliteTransaction? _batch;

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
    private const int SchemaVersion = 11;

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

        if (from < 8)
        {
            // Organisation, not content: a favourite belongs to a collection
            // without moving; a note is the entry's public face; a use count
            // remembers how often the entry came back.
            Execute("ALTER TABLE entries ADD COLUMN favorite INTEGER NOT NULL DEFAULT 0;");
            Execute("ALTER TABLE entries ADD COLUMN note TEXT NULL;");
            Execute("ALTER TABLE entries ADD COLUMN use_count INTEGER NOT NULL DEFAULT 0;");
        }

        if (from < 9)
        {
            // Groups are vertical containment — "this entry belongs to that
            // pile" — as against tags, which are horizontal labels for
            // filtering. Deleting a group orphans nothing: the foreign key
            // nulls the column and the entry simply returns to ungrouped.
            Execute("""
                CREATE TABLE IF NOT EXISTS groups (
                    id       INTEGER PRIMARY KEY AUTOINCREMENT,
                    name     TEXT    NOT NULL,
                    icon     TEXT    NULL,
                    position INTEGER NOT NULL DEFAULT 0,
                    hidden   INTEGER NOT NULL DEFAULT 0
                );
                """);
            Execute("ALTER TABLE entries ADD COLUMN group_id INTEGER NULL REFERENCES groups(id) ON DELETE SET NULL;");
            Execute("CREATE INDEX IF NOT EXISTS idx_entries_group ON entries (group_id);");
        }

        if (from < 10)
        {
            // A translation the user chose to keep, linked to the entry it
            // came from. The link is a reference, not a leash: delete the
            // original and the translation stays, merely unlinked.
            Execute("ALTER TABLE entries ADD COLUMN translated_from INTEGER NULL REFERENCES entries(id) ON DELETE SET NULL;");
        }

        if (from < 11)
        {
            // The original image's pixel size, so the preview panel can pick
            // its final size from the database without loading a thing. Rows
            // that predate the column get theirs read back out of the
            // thumbnail header — same aspect, and it is already in the row.
            Execute("ALTER TABLE entries ADD COLUMN image_width INTEGER NOT NULL DEFAULT 0;");
            Execute("ALTER TABLE entries ADD COLUMN image_height INTEGER NOT NULL DEFAULT 0;");
            BackfillImageSizes();
        }

        if (from != SchemaVersion)
        {
            Execute($"PRAGMA user_version = {SchemaVersion};");
        }

        BackfillSubtypes();
    }

    /// <summary>
    /// Fills <c>image_width</c>/<c>image_height</c> for image rows that have
    /// neither, from the thumbnail's IHDR chunk. Idempotent by the WHERE
    /// clause, so a database that arrives half-backfilled finishes the job on
    /// the next open.
    /// </summary>
    private void BackfillImageSizes()
    {
        using var read = _connection.CreateCommand();
        read.CommandText = "SELECT id, thumbnail FROM entries WHERE kind = 1 AND image_width = 0;";
        var pending = new List<(long Id, int Width, int Height)>();

        using (read)
        {
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetValue(1) is byte[] { Length: > 0 } png
                    && PngSize.Read(png) is { } size)
                {
                    pending.Add((reader.GetInt64(0), size.Width, size.Height));
                }
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        using var write = _connection.CreateCommand();
        write.CommandText = """
            UPDATE entries SET image_width = $w, image_height = $h WHERE id = $id;
            """;
        var id = write.CreateParameter();
        id.ParameterName = "$id";
        var width = write.CreateParameter();
        width.ParameterName = "$w";
        var height = write.CreateParameter();
        height.ParameterName = "$h";
        write.Parameters.AddRange([id, width, height]);

        foreach (var (rowId, w, h) in pending)
        {
            id.Value = rowId;
            width.Value = w;
            height.Value = h;
            write.ExecuteNonQuery();
        }
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
        lock (_gate)
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
        lock (_gate)
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
    }

    /// <summary>
    /// Whether the application has a row in the icon store — including a
    /// tombstone row for an icon that could not be found.
    /// </summary>
    public bool HasApplicationIcon(string name)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM applications WHERE name = $name;";
            command.Parameters.AddWithValue("$name", name);
            return Convert.ToInt64(command.ExecuteScalar()) > 0;
        }
    }

    /// <summary>The cached icon's PNG bytes, or null when none was found.</summary>
    public byte[]? ApplicationIcon(string name)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT icon FROM applications WHERE name = $name;";
            command.Parameters.AddWithValue("$name", name);
            var result = command.ExecuteScalar();
            return result is byte[] png ? png : null;
        }
    }

    /// <summary>
    /// Stores the icon row, ignoring the call when one already exists — see
    /// <see cref="SourceIconCache"/> for why a race must not overwrite.
    /// </summary>
    public void SaveApplicationIcon(string name, byte[]? png)
    {
        lock (_gate)
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
        DateTimeOffset createdAt,
        int width = 0,
        int height = 0)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO entries (text, source_app, created_at, kind, thumbnail, original_path, image_width, image_height)
                VALUES ($text, $sourceApp, $createdAt, $kind, $thumbnail, $originalPath, $w, $h)
                RETURNING id;
                """;
            command.Parameters.AddWithValue("$text", label);
            command.Parameters.AddWithValue("$sourceApp", (object?)sourceApp ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", createdAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$kind", (int)EntryKind.Image);
            command.Parameters.AddWithValue("$thumbnail", thumbnailPng);
            command.Parameters.AddWithValue("$originalPath", originalPath);
            command.Parameters.AddWithValue("$w", width);
            command.Parameters.AddWithValue("$h", height);

            var id = (long)command.ExecuteScalar()!;
            return new Entry(id, label, sourceApp, createdAt)
            {
                Kind = EntryKind.Image,
                ThumbnailPng = thumbnailPng,
                OriginalPath = originalPath,
                ImageWidth = width,
                ImageHeight = height,
            };
        }
    }

    /// <summary>
    /// Forgets where an original used to be, once retention has deleted it.
    /// The entry and its thumbnail are untouched.
    /// </summary>
    public void ClearOriginal(long id)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "UPDATE entries SET original_path = NULL WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Every image entry that still has an original on disk.</summary>
    public IReadOnlyList<Entry> ImagesWithOriginals(bool keepFavorites = false, bool keepPinned = false)
    {
        lock (_gate)
        {
            return ImagesWhere($"original_path IS NOT NULL AND NOT ({ProtectedConditionFor(keepFavorites, keepPinned)})");
        }
    }

    /// <summary>Image entries with originals, created within the range.</summary>
    public IReadOnlyList<Entry> ImagesCreatedBetween(
        DateTimeOffset from, DateTimeOffset to, bool keepFavorites = false, bool keepPinned = false)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files, favorite, note, use_count, group_id, translated_from, image_width, image_height,
                       (SELECT group_concat(t.name, char(31)) FROM tags t
                          JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
                FROM entries
                WHERE kind = $kind AND original_path IS NOT NULL
                  AND created_at BETWEEN $from AND $to
                  AND NOT ({ProtectedConditionFor(keepFavorites, keepPinned)});
                """;
            command.Parameters.AddWithValue("$kind", (int)EntryKind.Image);
            command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());

            return ReadEntries(command);
        }
    }

    private IReadOnlyList<Entry> ImagesWhere(string condition)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files, favorite, note, use_count, group_id, translated_from, image_width, image_height,
                   (SELECT group_concat(t.name, char(31)) FROM tags t
                      JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
            FROM entries
            WHERE kind = $kind AND {condition};
            """;
        command.Parameters.AddWithValue("$kind", (int)EntryKind.Image);

        return ReadEntries(command);
    }

    /// <summary>Image entries whose original is older than the cutoff.</summary>
    public IReadOnlyList<Entry> ImagesWithOriginalsBefore(
        DateTimeOffset cutoff, int limit, bool keepFavorites = false, bool keepPinned = false)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files, favorite, note, use_count, group_id, translated_from, image_width, image_height,
                       (SELECT group_concat(t.name, char(31)) FROM tags t
                          JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
                FROM entries
                WHERE kind = $kind AND original_path IS NOT NULL AND created_at < $cutoff
                  AND NOT ({ProtectedConditionFor(keepFavorites, keepPinned)})
                ORDER BY created_at ASC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$kind", (int)EntryKind.Image);
            command.Parameters.AddWithValue("$cutoff", cutoff.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$limit", limit);

            return ReadEntries(command);
        }
    }

    /// <summary>
    /// Writes many entries in one transaction. Appending them one by one costs
    /// a commit each, which turns a bulk write into a wait measured in minutes.
    /// </summary>
    public void AppendMany(IEnumerable<NewEntry> entries)
    {
        lock (_gate)
        {
            using var write = BeginWrite();
            using var command = _connection.CreateCommand();
            command.Transaction = write.Transaction;
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

            write.Commit();
        }
    }

    /// <summary>
    /// The last thing copied, or null when the history is empty. Ordered by
    /// time alone: the pipeline's duplicate check asks "was this the previous
    /// copy", and a pinned entry from last week never is.
    /// </summary>
    public Entry? MostRecent()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files, favorite, note, use_count, group_id, translated_from, image_width, image_height,
                       (SELECT group_concat(t.name, char(31)) FROM tags t
                          JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
                FROM entries
                ORDER BY created_at DESC, id DESC
                LIMIT 1;
                """;

            return ReadEntries(command).FirstOrDefault();
        }
    }

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
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return [];
            }

            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files, favorite, note, use_count, group_id, translated_from, image_width, image_height,
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
        lock (_gate)
        {
            var conditions = new List<string>();

            using var command = _connection.CreateCommand();

            BuildFilterConditions(filter, command, conditions);

            var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);

            command.CommandText = $"""
                SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files, favorite, note, use_count, group_id, translated_from, image_width, image_height,
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
    }

    /// <summary>
    /// How many entries the filter matches in total — the number a filtered
    /// list's footer owes the user ("3 / 161 条"), which a paged list cannot
    /// know from memory: it only holds the loaded page.
    /// </summary>
    public int CountMatching(HistoryFilter filter)
    {
        lock (_gate)
        {
            var conditions = new List<string>();

            using var command = _connection.CreateCommand();

            BuildFilterConditions(filter, command, conditions);

            var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);

            command.CommandText = $"SELECT COUNT(*) FROM entries {where};";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    /// <summary>
    /// The WHERE clauses a filter compiles to, with their parameters bound onto
    /// <paramref name="command"/>. Shared by the paged reader and the counter
    /// so the two can never disagree about what a filter means.
    /// </summary>
    private static void BuildFilterConditions(HistoryFilter filter, SqliteCommand command, List<string> conditions)
    {
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            // The note is searchable alongside the text: "the brand blue one"
            // has to find the entry whose content is a bare hex code.
            conditions.Add("(text LIKE $pattern ESCAPE '\\' OR note LIKE $pattern ESCAPE '\\')");
            command.Parameters.AddWithValue("$pattern", $"%{EscapeForLike(filter.Query)}%");
        }

        if (filter.Favorite is { } favoriteOnly && favoriteOnly)
        {
            conditions.Add("favorite = 1");
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

        if (filter.Group is { } group)
        {
            conditions.Add("group_id = $group");
            command.Parameters.AddWithValue("$group", group);
        }
    }

    /// <summary>
    /// A window onto the history, newest first. Every read path takes a limit:
    /// the history is never loaded into memory in one piece, however large it
    /// grows.
    /// </summary>
    public IReadOnlyList<Entry> Page(int limit, int offset)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files, favorite, note, use_count, group_id, translated_from, image_width, image_height,
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
    }

    public int Count()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM entries;";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    /// <summary>
    /// How many entries the current protection settings would spare — the
    /// number a confirmation owes the user before a bulk delete.
    /// </summary>
    public int CountProtected(bool keepFavorites, bool keepPinned)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM entries WHERE {ProtectedConditionFor(keepFavorites, keepPinned)};";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    /// <summary>The protected entries inside a time range — for a range delete's confirmation copy.</summary>
    public int CountProtectedBetween(
        DateTimeOffset from, DateTimeOffset to, bool keepFavorites, bool keepPinned)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                SELECT COUNT(*) FROM entries
                WHERE created_at BETWEEN $from AND $to
                  AND {ProtectedConditionFor(keepFavorites, keepPinned)};
                """;
            command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    /// <summary>Returns whether there was anything to delete.</summary>
    public bool Delete(long id)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM entries WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>
    /// Deletes entries created within the range, both ends included.
    ///
    /// Protected entries — favourites and pins, when their switches are on —
    /// are spared: bulk deletes are the delete a user does not look at each
    /// row of, which is exactly where a marker worth keeping should count.
    /// </summary>
    public int DeleteCreatedBetween(
        DateTimeOffset from, DateTimeOffset to, bool keepFavorites = false, bool keepPinned = false)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                DELETE FROM entries
                WHERE created_at BETWEEN $from AND $to
                  AND NOT ({ProtectedConditionFor(keepFavorites, keepPinned)});
                """;
            command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
            return command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// The SQL that says an entry is protected — literally, so any caller can
    /// embed it without binding parameters by hand. The whole OR chain is
    /// wrapped: unwrapped, SQL precedence would read
    /// "range AND favourite" OR "pinned anywhere", which is not the promise.
    /// </summary>
    private static string ProtectedConditionFor(bool keepFavorites, bool keepPinned)
    {
        var clauses = new List<string>();
        if (keepFavorites)
        {
            clauses.Add("(favorite = 1)");
        }

        if (keepPinned)
        {
            clauses.Add("(pinned = 1)");
        }

        return clauses.Count == 0 ? "0" : $"({string.Join(" OR ", clauses)})";
    }

    /// <summary>
    /// Clears the history. Protected entries stay when their switches are on —
    /// "clear everything" is exactly the moment a user would rather keep the
    /// pile they so carefully starred — and the confirmation copy says so.
    /// </summary>
    public int DeleteAll(bool keepFavorites = false, bool keepPinned = false)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"DELETE FROM entries WHERE NOT ({ProtectedConditionFor(keepFavorites, keepPinned)});";
            return command.ExecuteNonQuery();
        }
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
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "UPDATE entries SET created_at = $createdAt WHERE id = $id;";
            command.Parameters.AddWithValue("$createdAt", at.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<Entry> Recent(int limit)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files, favorite, note, use_count, group_id, translated_from, image_width, image_height,
                       (SELECT group_concat(t.name, char(31)) FROM tags t
                          JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
                FROM entries
                ORDER BY pinned DESC, created_at DESC, id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);

            return ReadEntries(command);
        }
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
                Favorite = reader.GetInt32(12) != 0,
                Note = reader.IsDBNull(13) ? null : reader.GetString(13),
                UseCount = reader.GetInt32(14),
                GroupId = reader.IsDBNull(15) ? null : reader.GetInt64(15),
                TranslatedFrom = reader.IsDBNull(16) ? null : reader.GetInt64(16),
                ImageWidth = reader.GetInt32(17),
                ImageHeight = reader.GetInt32(18),

                // Joined in rather than fetched per row: a list of a hundred
                // entries would otherwise be a hundred extra queries.
                Tags = reader.IsDBNull(19)
                    ? []
                    : reader.GetString(19).Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries),
            });
        }

        return entries;
    }

    /// <summary>
    /// Runs <paramref name="work"/> as one transaction: every write inside it
    /// lands together, or — when it throws — none does. Members that would
    /// open a transaction of their own join this one instead, since SQLite
    /// cannot nest them. The gate is held throughout and every other caller
    /// waits, so the work should be writes already prepared — never reading
    /// files or waiting on anything.
    /// </summary>
    internal void RunInTransaction(Action work)
    {
        lock (_gate)
        {
            if (_batch is not null)
            {
                work();
                return;
            }

            using var transaction = _connection.BeginTransaction();
            _batch = transaction;
            try
            {
                work();
                transaction.Commit();
            }
            finally
            {
                _batch = null;
            }
        }
    }

    /// <summary>A write that spans statements: inside the open batch when there is one, else in its own transaction.</summary>
    private WriteScope BeginWrite()
        => _batch is { } open
            ? new WriteScope(open, owned: false)
            : new WriteScope(_connection.BeginTransaction(), owned: true);

    private readonly struct WriteScope(SqliteTransaction transaction, bool owned) : IDisposable
    {
        public SqliteTransaction Transaction => transaction;

        /// <summary>A joined write commits when its batch does, never on its own.</summary>
        public void Commit()
        {
            if (owned)
            {
                transaction.Commit();
            }
        }

        public void Dispose()
        {
            if (owned)
            {
                transaction.Dispose();
            }
        }
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }
}
