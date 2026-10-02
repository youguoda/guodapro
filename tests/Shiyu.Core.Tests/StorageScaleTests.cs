using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// Ticket 12 (O-22): storage designed for a history measured in the hundreds
/// of thousands. The S items — ordering index, subtype sentinel, count cache —
/// each change something observable at the storage layer, so each is pinned
/// here at that layer rather than through a window.
/// </summary>
public class StorageScaleTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    // --- the ordering index ---------------------------------------------------

    [Fact]
    public void The_ordering_index_exists_after_an_upgrade_from_version_11()
    {
        using var database = new TempDatabase();

        // Written by the current build, then stamped back to 11: a database
        // the previous release left behind, without hand-building its schema.
        using (var store = EntryStore.Open(database.FilePath))
        {
            store.Append("written before the upgrade", "test", Noon);
            using var stamp = store.Connection.CreateCommand();
            stamp.CommandText = "PRAGMA user_version = 11;";
            stamp.ExecuteNonQuery();
        }

        using var upgraded = EntryStore.Open(database.FilePath);

        using var check = upgraded.Connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'idx_entries_order';";
        Assert.Equal(1L, check.ExecuteScalar());
    }

    // --- the subtype sentinel ---------------------------------------------------

    [Fact]
    public void Plain_text_stores_a_sentinel_rather_than_null()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        store.Append("普通文本", "test", Noon);
        store.Append("https://example.com", "test", Noon.AddSeconds(1));
        store.AppendTranslation("翻译过的句子", "Shiyu", Noon.AddSeconds(2), null);
        store.AppendMany([new NewEntry("批量写入", "test", Noon.AddSeconds(3))]);

        // Every write path files a classified row; NULL is a state only older
        // builds could leave, and only the migration touches those.
        using var command = store.Connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM entries WHERE sub_type IS NULL AND kind = 0;";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public void Rows_left_null_by_an_older_build_are_backfilled_once()
    {
        using var database = new TempDatabase();
        var afterUpgrade = (object?)null;

        using (var store = EntryStore.Open(database.FilePath))
        {
            // Written the way an older build would have: no subtype, and a
            // database version to match.
            using var insert = store.Connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO entries (text, source_app, created_at, kind)
                VALUES ('https://legacy.example.com', 'old', 0, 0),
                       ('很普通的一段文字', 'old', 1, 0);
                """;
            insert.ExecuteNonQuery();

            using var stamp = store.Connection.CreateCommand();
            stamp.CommandText = "PRAGMA user_version = 11;";
            stamp.ExecuteNonQuery();
        }

        using (var upgraded = EntryStore.Open(database.FilePath))
        {
            // The classified row keeps its subtype, the plain row its sentinel
            // — one pass, and nothing text-shaped is left NULL.
            Assert.Equal(EntrySubtype.Link, upgraded.Page(10, 0).First(e => e.Text.StartsWith("https")).Subtype);
            Assert.Equal(EntrySubtype.None, upgraded.Page(10, 0).First(e => !e.Text.StartsWith("https")).Subtype);

            using var command = upgraded.Connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM entries WHERE sub_type IS NULL AND kind = 0;";
            Assert.Equal(0L, command.ExecuteScalar());

            command.CommandText = "PRAGMA user_version;";
            afterUpgrade = command.ExecuteScalar();
        }

        // And never again: the second open has nothing to migrate and the
        // version stays put, which is what retired the startup rescan.
        using (var reopened = EntryStore.Open(database.FilePath))
        {
            using var command = reopened.Connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(afterUpgrade, command.ExecuteScalar());
        }
    }

    // --- the count cache ---------------------------------------------------

    [Fact]
    public void Count_tracks_writes_through_the_cache()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        Assert.Equal(0, store.Count());

        var first = store.Append("one", "test", Noon);
        store.AppendImage("图片 10×10", [1], "unused.png", "test", Noon.AddSeconds(1));
        store.AppendFiles(["C:\\a.txt"], "test", Noon.AddSeconds(2));
        store.AppendTranslation("译文", "Shiyu", Noon.AddSeconds(3), null);
        store.AppendMany([new NewEntry("bulk", "test", Noon.AddSeconds(4))]);
        store.ImportEntry(new Entry(0, "imported", "test", Noon.AddSeconds(5)), groupName: null);

        Assert.Equal(6, store.Count());

        store.Delete(first.Id);
        Assert.Equal(5, store.Count());

        store.DeleteAll();
        Assert.Equal(0, store.Count());

        store.ImportEntry(new Entry(0, "restored", "test", Noon.AddSeconds(6)), groupName: null);
        Assert.Equal(1, store.Count());
    }

    // --- the payload side table (O-22, ticket 12 M) ---------------------------

    /// <summary>
    /// A hand-built version-12 library: the old layout with thumbnail/html/rtf
    /// inline in the entries table. The shape an upgrade actually meets.
    /// </summary>
    private static void CreateVersion12Library(string path, byte[] thumbnail, string html)
    {
        using var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        old.Open();
        using var create = old.CreateCommand();
        create.CommandText = """
            CREATE TABLE entries (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                text        TEXT    NOT NULL,
                source_app  TEXT    NULL,
                created_at  INTEGER NOT NULL,
                kind        INTEGER NOT NULL DEFAULT 0,
                thumbnail   BLOB    NULL,
                original_path TEXT  NULL,
                pinned      INTEGER NOT NULL DEFAULT 0,
                sub_type    TEXT    NULL,
                html        TEXT    NULL,
                rtf         TEXT    NULL,
                files       TEXT    NULL,
                favorite    INTEGER NOT NULL DEFAULT 0,
                note        TEXT    NULL,
                use_count   INTEGER NOT NULL DEFAULT 0,
                group_id    INTEGER NULL,
                translated_from INTEGER NULL,
                image_width INTEGER NOT NULL DEFAULT 0,
                image_height INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE tags (
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE
            );
            CREATE TABLE entry_tags (
                entry_id INTEGER NOT NULL REFERENCES entries(id) ON DELETE CASCADE,
                tag_id   INTEGER NOT NULL REFERENCES tags(id)    ON DELETE CASCADE,
                PRIMARY KEY (entry_id, tag_id)
            );
            CREATE INDEX idx_entry_tags_tag ON entry_tags (tag_id);
            CREATE TABLE groups (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                name     TEXT    NOT NULL,
                icon     TEXT    NULL,
                position INTEGER NOT NULL DEFAULT 0,
                hidden   INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX idx_entries_created_at ON entries (created_at DESC);
            PRAGMA user_version = 12;

            INSERT INTO entries (text, source_app, created_at, kind, thumbnail, original_path, sub_type, html, image_width, image_height)
            VALUES ('图片 32×32', 'brush', 1758542400000, 1, $png, 'C:\\gone.png', 'None', NULL, 32, 32);
            INSERT INTO entries (text, source_app, created_at, sub_type, html)
            VALUES ('富文本', 'word', 1758542400001, 'None', $html);
            INSERT INTO entries (text, source_app, created_at, sub_type)
            VALUES ('普通文本', 'notepad', 1758542400002, 'None');
            """;
        create.Parameters.AddWithValue("$png", thumbnail);
        create.Parameters.AddWithValue("$html", html);
        create.ExecuteNonQuery();
    }

    [Fact]
    public void An_inline_payload_library_upgrades_without_losing_a_byte()
    {
        using var database = new TempDatabase();
        var thumbnail = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var html = "<p>带格式的<b>旧库</b>内容</p>";
        CreateVersion12Library(database.FilePath, thumbnail, html);

        using var store = EntryStore.Open(database.FilePath);

        var rows = store.Page(10, 0).ToDictionary(entry => entry.Text);

        // Payloads travel to the side table and come back through the reads
        // that ask for them; every marker column survives the rewrite.
        Assert.Equal(thumbnail, store.Get(rows["图片 32×32"].Id)!.ThumbnailPng);
        Assert.Equal(html, store.Get(rows["富文本"].Id)!.Html);
        Assert.Null(store.Get(rows["普通文本"].Id)!.Html);
        Assert.Equal(EntryKind.Image, rows["图片 32×32"].Kind);
        Assert.Equal(32, rows["图片 32×32"].ImageWidth);

        // The old columns are gone from the schema, not merely ignored.
        using var columns = store.Connection.CreateCommand();
        columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('entries') WHERE name IN ('thumbnail', 'html', 'rtf');";
        Assert.Equal(0L, columns.ExecuteScalar());

        using var sideRows = store.Connection.CreateCommand();
        sideRows.CommandText = "SELECT COUNT(*) FROM entry_blobs;";
        Assert.Equal(2L, sideRows.ExecuteScalar());

        // And the upgrade is durable: the second open is an ordinary one.
        using (var reopened = EntryStore.Open(database.FilePath))
        {
            Assert.Equal(3, reopened.Count());
            Assert.Equal(html, reopened.Get(rows["富文本"].Id)!.Html);
        }
    }

    [Fact]
    public void A_payload_migration_interrupted_midway_finishes_on_the_next_open()
    {
        using var database = new TempDatabase();
        var thumbnail = new byte[] { 9, 9, 9, 9 };
        CreateVersion12Library(database.FilePath, thumbnail, "<p>half moved</p>");

        // What a killed upgrade leaves behind: the side table exists, the
        // first row's payload has moved and its source columns are clear, the
        // rest still carry theirs inline — and the version says 12.
        using (var interrupted = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database.FilePath}"))
        {
            interrupted.Open();
            using var partial = interrupted.CreateCommand();
            partial.CommandText = """
                CREATE TABLE entry_blobs (
                    entry_id  INTEGER PRIMARY KEY REFERENCES entries(id) ON DELETE CASCADE,
                    thumbnail BLOB NULL,
                    html      TEXT NULL,
                    rtf       TEXT NULL
                );
                INSERT INTO entry_blobs (entry_id, thumbnail, html, rtf)
                SELECT id, thumbnail, html, rtf FROM entries WHERE id = 1;
                UPDATE entries SET thumbnail = NULL, html = NULL, rtf = NULL WHERE id = 1;
                """;
            partial.ExecuteNonQuery();
        }

        using var store = EntryStore.Open(database.FilePath);

        var rows = store.Page(10, 0).ToDictionary(entry => entry.Text);

        // The already-moved row kept its payload, the interrupted ones arrived
        // with theirs — nothing was copied twice and nothing was dropped.
        Assert.Equal(thumbnail, store.Get(rows["图片 32×32"].Id)!.ThumbnailPng);
        Assert.Equal("<p>half moved</p>", store.Get(rows["富文本"].Id)!.Html);
        Assert.Equal(3, store.Count());

        using var version = store.Connection.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(13L, version.ExecuteScalar());
    }

    [Fact]
    public void Payloads_are_fetched_for_a_page_by_id_and_by_cursor_for_whole_history_walks()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        var image = store.AppendImage("图片 4×4", [7, 7, 7], "C:\\x.png", "test", Noon);
        var rich = store.Append("富文本", "test", Noon.AddSeconds(1), html: "<p>x</p>");
        store.Append("普通", "test", Noon.AddSeconds(2));

        // The page read: only the ids a page actually shows.
        var blobs = store.BlobsOf([image.Id, rich.Id]);
        Assert.Equal([7, 7, 7], blobs[image.Id].ThumbnailPng);
        Assert.Equal("<p>x</p>", blobs[rich.Id].Html);
        Assert.Null(blobs[rich.Id].ThumbnailPng);

        // The whole-history walk, by id cursor — the export and merge shape.
        var walked = new List<Entry>();
        long after = 0;
        while (true)
        {
            var page = store.EntriesWithBlobsAfter(after, 2);
            if (page.Count == 0)
            {
                break;
            }

            walked.AddRange(page);
            after = page[^1].Id;
        }

        Assert.Equal(3, walked.Count);
        Assert.Equal([7, 7, 7], walked.Single(entry => entry.Kind == EntryKind.Image).ThumbnailPng);
        Assert.Equal("<p>x</p>", walked.Single(entry => entry.Text == "富文本").Html);
        Assert.Equal(image.Id, walked[0].Id);
    }
}
