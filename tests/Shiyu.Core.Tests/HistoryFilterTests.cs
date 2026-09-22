using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class HistoryFilterTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static EntryStore Seeded(TempDatabase database)
    {
        var store = EntryStore.Open(database.FilePath);
        store.Append("a note from last month", "notepad", Noon.AddDays(-32));
        store.Append("a note from last week", "notepad", Noon.AddDays(-6));
        store.Append("a note from today", "notepad", Noon);
        store.AppendImage("图片 800×600", [1], "C:/nowhere/old.png", "snipping", Noon.AddDays(-6));
        store.AppendImage("图片 640×480", [2], "C:/nowhere/new.png", "snipping", Noon);
        return store;
    }

    [Fact]
    public void An_empty_filter_returns_everything()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database);

        Assert.Equal(5, store.Find(HistoryFilter.None, limit: 50).Count);
    }

    [Fact]
    public void Filtering_by_type_narrows_to_that_type()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database);

        var images = store.Find(new HistoryFilter { Kind = EntryKind.Image }, limit: 50);

        Assert.Equal(2, images.Count);
        Assert.All(images, entry => Assert.Equal(EntryKind.Image, entry.Kind));
    }

    [Fact]
    public void Filtering_by_date_range_excludes_both_sides()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database);

        var lastWeek = store.Find(
            new HistoryFilter { From = Noon.AddDays(-7), To = Noon.AddDays(-1) },
            limit: 50);

        Assert.Equal(2, lastWeek.Count);
        Assert.All(lastWeek, entry => Assert.InRange(entry.CreatedAt, Noon.AddDays(-7), Noon.AddDays(-1)));
    }

    [Fact]
    public void A_keyword_and_a_date_range_narrow_together()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database);

        var found = store.Find(
            new HistoryFilter { Query = "note", From = Noon.AddDays(-7) },
            limit: 50);

        // Combining has to narrow the whole history — not just whatever the
        // keyword happened to return first.
        Assert.Equal(2, found.Count);
        Assert.All(found, entry => Assert.Contains("note", entry.Text));
    }

    [Fact]
    public void That_image_from_last_week_is_findable_with_type_and_date_together()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database);

        var found = store.Find(
            new HistoryFilter
            {
                Kind = EntryKind.Image,
                From = Noon.AddDays(-7),
                To = Noon.AddDays(-1),
            },
            limit: 50);

        var entry = Assert.Single(found);
        Assert.Equal("图片 800×600", entry.Text);
    }

    [Fact]
    public void Wildcards_in_a_filtered_query_are_still_taken_literally()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        store.Append("100% done", "test", Noon);
        store.Append("nothing alike", "test", Noon);

        var found = store.Find(new HistoryFilter { Query = "100%" }, limit: 50);

        // An unescaped "%" here would quietly return the whole history.
        var entry = Assert.Single(found);
        Assert.Equal("100% done", entry.Text);
    }

    [Fact]
    public void A_filter_that_matches_nothing_returns_nothing()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database);

        Assert.Empty(store.Find(new HistoryFilter { Query = "nowhere to be found" }, limit: 50));
    }

    [Fact]
    public void Results_stay_newest_first_however_they_were_narrowed()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database);

        var found = store.Find(new HistoryFilter { Query = "note" }, limit: 50);

        Assert.Equal(
            new[] { "a note from today", "a note from last week", "a note from last month" },
            found.Select(entry => entry.Text));
    }

    [Fact]
    public void A_filter_with_nothing_set_reports_itself_as_empty()
    {
        Assert.True(HistoryFilter.None.IsEmpty);
        Assert.True(new HistoryFilter { Query = "   " }.IsEmpty);
        Assert.False(new HistoryFilter { Kind = EntryKind.Image }.IsEmpty);
    }
}
