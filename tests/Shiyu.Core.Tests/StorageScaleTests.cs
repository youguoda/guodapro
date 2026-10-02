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
            Assert.Equal(12L, command.ExecuteScalar());
        }

        // And never again: the second open has nothing to migrate and the
        // version stays put, which is what retired the startup rescan.
        using (var reopened = EntryStore.Open(database.FilePath))
        {
            using var command = reopened.Connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(12L, command.ExecuteScalar());
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
}
