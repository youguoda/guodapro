using System.Diagnostics;
using System.Text;
using Shiyu.Core;

// Ticket 12 / O-22: the three numbers the ticket promised to hold on a
// hundred-thousand-entry mixed library — startup (open + migrate + first
// page), one narrow-bar search, one copy's ingest (append + touch) — plus the
// one-time cost of upgrading a version-11 library (inline blobs, no subtype
// sentinel, no index, no FTS) to the current schema.
//
// Everything runs on a throwaway database under %TEMP%; nothing here touches
// the real Shiyu data directory.

Console.OutputEncoding = Encoding.UTF8;

const int Total = 100_000;

var root = Path.Combine(Path.GetTempPath(), "shiyu-bench", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var current = Path.Combine(root, "history.db");
var legacy = Path.Combine(root, "legacy.db");

Console.WriteLine($"[bench] database root: {root}");
Console.WriteLine($"[bench] bundled SQLite {SqliteVersion(current)}");

GenerateCurrent(current, Total);
GenerateLegacy(legacy, Total);

// --- 1. startup: open + migrate + the first page the bar would show --------
// Honest about what this is: a warm-OS-file-cache measurement (dropping the
// page cache needs privileges the bench should not have); the number to
// compare against the <1s budget, with cold-disk reality being several times
// worse for every implementation alike.

for (var attempt = 1; attempt <= 3; attempt++)
{
    var watch = Stopwatch.StartNew();
    using (var store = EntryStore.Open(current))
    {
        _ = store.Count();
        var page = store.Recent(100);
        if (page.Count != 100)
        {
            throw new InvalidOperationException("first page came back short");
        }
    }

    watch.Stop();
    Console.WriteLine($"[startup] open + migrate + first page of 100: {watch.ElapsedMilliseconds} ms (run {attempt})");
}

// --- 2. search: the narrow bar's Find, and the two Search paths -------------

using (var store = EntryStore.Open(current))
{
    var needle = "明察秋毫的查找目标";
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        var watch = Stopwatch.StartNew();
        var found = store.Find(new HistoryFilter { Query = needle }, 100);
        watch.Stop();

        Console.WriteLine($"[find] trigram query ({found.Count} hits), page of {Math.Min(100, found.Count)}: {watch.Elapsed.TotalMilliseconds:F1} ms (run {attempt})");
    }

    for (var attempt = 1; attempt <= 3; attempt++)
    {
        var watch = Stopwatch.StartNew();
        var found = store.Search("富文本条目", 60);
        watch.Stop();

        Console.WriteLine($"[search] trigram ({found.Count} hits), page of {Math.Min(60, found.Count)}: {watch.Elapsed.TotalMilliseconds:F1} ms (run {attempt})");
    }

    for (var attempt = 1; attempt <= 3; attempt++)
    {
        var watch = Stopwatch.StartNew();
        var found = store.Search("普通", 60);
        watch.Stop();

        Console.WriteLine($"[search] two-character LIKE fallback ({found.Count} hits): {watch.Elapsed.TotalMilliseconds:F1} ms (run {attempt})");
    }

    // --- 3. one copy's ingest: append + touch, the pipeline's pair ----------

    var at = DateTimeOffset.UtcNow;
    var samples = new List<double>();
    for (var copy = 0; copy < 50; copy++)
    {
        var watch = Stopwatch.StartNew();
        var entry = store.Append($"bench copy {copy} 基准复制的第 {copy} 条", "bench", at.AddMilliseconds(copy));
        store.Touch(entry.Id, at.AddMilliseconds(copy + 1));
        watch.Stop();
        samples.Add(watch.Elapsed.TotalMilliseconds);
    }

    Console.WriteLine(
        $"[ingest] append + touch over 50 copies: avg {samples.Average():F2} ms, max {samples.Max():F2} ms");
}

// --- 4. the one-time upgrade: version 11 (inline blobs) to current ----------

{
    var watch = Stopwatch.StartNew();
    using var store = EntryStore.Open(legacy);
    var page = store.Recent(100);
    watch.Stop();

    Console.WriteLine($"[upgrade] v11 (inline payloads, no index, no FTS) -> current, {page.Count} entries readable: {watch.Elapsed.TotalSeconds:F1} s");

    var search = store.Find(new HistoryFilter { Query = "富文本条目" }, 100);
    Console.WriteLine($"[upgrade] search works immediately after: {search.Count} hits");
}

Console.WriteLine("[bench] done");
return 0;

// --- generation ------------------------------------------------------------

static string SqliteVersion(string path)
{
    using var probe = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
    probe.Open();
    using var command = probe.CreateCommand();
    command.CommandText = "SELECT sqlite_version();";
    return command.ExecuteScalar()!.ToString()!;
}

/// <summary>
/// The current schema's library, written through the public store in batched
/// transactions: 10% images with 20-40 KB thumbnails, 20% rich text with tens
/// of KB of HTML, 70% plain text mixed Chinese and English, 1% pinned, and
/// one needle near the very bottom for search to find.
/// </summary>
static void GenerateCurrent(string path, int total)
{
    var watch = Stopwatch.StartNew();
    var random = new Random(20261001);
    var at = DateTimeOffset.UtcNow.AddDays(-90);

    using (var store = EntryStore.Open(path))
    {
        var written = 0;
        while (written < total)
        {
            store.RunInTransaction(() =>
            {
                for (var n = 0; n < 1_000 && written < total; n++, written++)
                {
                    var bucket = written % 10;
                    if (bucket == 0)
                    {
                        var thumbnail = new byte[random.Next(20_000, 40_000)];
                        random.NextBytes(thumbnail);
                        store.AppendImage(
                            $"image {written} 240x180", thumbnail, $"C:\\imgs\\{written}.png",
                            "bench", at.AddSeconds(written), 240, 180);
                    }
                    else if (bucket is 1 or 2)
                    {
                        var html = "<p>" + string.Concat(Enumerable.Repeat(
                            $"rich paragraph {written} 这是一段富文本段落内容 ", 900)) + "</p>";
                        store.Append($"rich text {written} 富文本条目", "bench", at.AddSeconds(written), html: html);
                    }
                    else
                    {
                        store.Append(
                            $"ordinary entry {written} 普通条目编号 {written * 7}",
                            "bench", at.AddSeconds(written));
                    }
                }
            });
        }

        // The oldest rows are the hardest to reach; the needle goes there.
        store.Append("这是埋在最底下、明察秋毫的查找目标", "bench", at.AddSeconds(-1));

        foreach (var pinned in store.Page(200).Where(entry => entry.Kind == EntryKind.Text).Take(100).ToList())
        {
            store.SetPinned(pinned.Id, true);
        }
    }

    watch.Stop();
    var megabytes = new FileInfo(path).Length / 1024.0 / 1024.0;
    Console.WriteLine($"[gen] current library: {total + 1} entries in {watch.Elapsed.TotalSeconds:F1} s ({megabytes:F0} MB)");
}

/// <summary>
/// A version-11 shape: thumbnail/html/rtf inline, NULL subtypes, no ordering
/// index, no FTS — written with raw SQL because the current store cannot
/// produce that shape. The upgrade benches what a real user's upgrade pays
/// once: blob split + VACUUM + trigram rebuild.
/// </summary>
static void GenerateLegacy(string path, int total)
{
    var watch = Stopwatch.StartNew();
    var random = new Random(20261002);
    var at = DateTimeOffset.UtcNow.AddDays(-90);

    using var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
    old.Open();

    using (var create = old.CreateCommand())
    {
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
            CREATE TABLE applications (name TEXT PRIMARY KEY, icon BLOB NULL);
            CREATE INDEX idx_entries_created_at ON entries (created_at DESC);
            CREATE INDEX idx_entries_group ON entries (group_id);
            PRAGMA user_version = 11;
            """;
        create.ExecuteNonQuery();
    }

    using (var insert = old.CreateCommand())
    {
        insert.CommandText = """
            INSERT INTO entries (text, source_app, created_at, kind, thumbnail, sub_type, html, image_width, image_height)
            VALUES ($text, $source, $at, $kind, $thumb, NULL, $html, $w, $h);
            """;
        var text = insert.Parameters.Add("$text", Microsoft.Data.Sqlite.SqliteType.Text);
        var source = insert.Parameters.Add("$source", Microsoft.Data.Sqlite.SqliteType.Text);
        var createdAt = insert.Parameters.Add("$at", Microsoft.Data.Sqlite.SqliteType.Integer);
        var kind = insert.Parameters.Add("$kind", Microsoft.Data.Sqlite.SqliteType.Integer);
        var thumb = insert.Parameters.Add("$thumb", Microsoft.Data.Sqlite.SqliteType.Blob);
        var html = insert.Parameters.Add("$html", Microsoft.Data.Sqlite.SqliteType.Text);
        var width = insert.Parameters.Add("$w", Microsoft.Data.Sqlite.SqliteType.Integer);
        var height = insert.Parameters.Add("$h", Microsoft.Data.Sqlite.SqliteType.Integer);

        using var transaction = old.BeginTransaction();
        insert.Transaction = transaction;

        for (var i = 0; i < total; i++)
        {
            var bucket = i % 10;
            text.Value = bucket == 0
                ? $"image {i} 240x180"
                : bucket is 1 or 2
                    ? $"rich text {i} 富文本条目"
                    : $"ordinary entry {i} 普通条目编号 {i * 7}";
            source.Value = "bench";
            createdAt.Value = at.AddSeconds(i).ToUnixTimeMilliseconds();
            kind.Value = bucket == 0 ? 1 : 0;
            thumb.Value = bucket == 0
                ? RandomBytes(random, 20_000, 40_000)
                : DBNull.Value;
            html.Value = bucket is 1 or 2
                ? "<p>" + string.Concat(Enumerable.Repeat($"rich paragraph {i} 这是一段富文本段落内容 ", 900)) + "</p>"
                : DBNull.Value;
            width.Value = bucket == 0 ? 240 : 0;
            height.Value = bucket == 0 ? 180 : 0;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    watch.Stop();
    var megabytes = new FileInfo(path).Length / 1024.0 / 1024.0;
    Console.WriteLine($"[gen] legacy v11 library: {total} entries in {watch.Elapsed.TotalSeconds:F1} s ({megabytes:F0} MB, payloads inline)");
}

static byte[] RandomBytes(Random random, int minimum, int maximum)
{
    var bytes = new byte[random.Next(minimum, maximum)];
    random.NextBytes(bytes);
    return bytes;
}
