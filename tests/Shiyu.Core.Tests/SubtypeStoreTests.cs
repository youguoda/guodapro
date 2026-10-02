using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// The subtype must survive the store round trip, combine with the other
/// filters, and be filled in for rows recorded before it existed.
/// </summary>
public class SubtypeStoreTests
{
    [Fact]
    public void Subtypes_survive_the_round_trip()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        store.Append("https://example.com", "chrome", DateTimeOffset.UnixEpoch);
        store.Append("#1F6FEB", "paint", DateTimeOffset.UnixEpoch);
        store.Append("普通文本", "notepad", DateTimeOffset.UnixEpoch);

        var entries = store.Page(10);

        Assert.Equal(EntrySubtype.Link, entries.First(e => e.Text.StartsWith("https")).Subtype);
        Assert.Equal(EntrySubtype.Color, entries.First(e => e.Text == "#1F6FEB").Subtype);
        Assert.Equal(EntrySubtype.None, entries.First(e => e.Text == "普通文本").Subtype);
    }

    [Fact]
    public void Subtype_filtering_combines_with_keyword_and_kind()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        store.Append("https://example.com/a", "chrome", DateTimeOffset.UnixEpoch);
        store.Append("https://example.com/b", "chrome", DateTimeOffset.UnixEpoch);
        store.Append("https://other.org/c", "chrome", DateTimeOffset.UnixEpoch);

        var found = store.Find(
            new HistoryFilter { Subtype = EntrySubtype.Link, Query = "example" },
            limit: 10);

        Assert.Equal(2, found.Count);
        Assert.All(found, entry => Assert.Equal(EntrySubtype.Link, entry.Subtype));
    }

    [Fact]
    public void The_path_filter_bucket_covers_both_local_and_unc()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        store.Append(@"C:\temp\a.txt", "explorer", DateTimeOffset.UnixEpoch);
        store.Append(@"\\server\share\b.txt", "explorer", DateTimeOffset.UnixEpoch);
        store.Append("plain text", "notepad", DateTimeOffset.UnixEpoch);

        var found = store.Find(
            new HistoryFilter { Subtype = EntrySubtype.LocalPath },
            limit: 10);

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void Rows_recorded_before_subtypes_exist_are_backfilled_on_upgrade()
    {
        using var database = new TempDatabase();

        using (var store = EntryStore.Open(database.FilePath))
        {
            // Written the way an older build would have: no subtype at all,
            // and a schema version to match — the backfill is a migration now
            // (O-22), not something every open repeats.
            using var command = store.Connection.CreateCommand();
            command.CommandText = """
                INSERT INTO entries (text, source_app, created_at, kind)
                VALUES ('https://legacy.example.com', 'old', 0, 0);
                PRAGMA user_version = 11;
                """;
            command.ExecuteNonQuery();
        }

        using (var reopened = EntryStore.Open(database.FilePath))
        {
            var entry = Assert.Single(reopened.Page(10));
            Assert.Equal(EntrySubtype.Link, entry.Subtype);
        }
    }
}
