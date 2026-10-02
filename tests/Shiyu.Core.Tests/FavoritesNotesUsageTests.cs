using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// Three organisation abilities with one rule each: a favourite changes
/// belonging, never position; a note is searchable and is the entry's public
/// face; a usage count only ever goes up.
/// </summary>
public class FavoritesNotesUsageTests
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Favouriting_does_not_move_the_entry()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        var older = store.Append("older", null, Monday);
        var newer = store.Append("newer", null, Tuesday);

        store.SetFavorite(older.Id, true);

        // Newest first both before and after: a favourite belongs to a
        // collection, it does not claim the top of the list — that is the
        // pin's job.
        Assert.Equal(["newer", "older"], store.Page(10).Select(e => e.Text));
        Assert.True(store.Page(10).Single(e => e.Text == "older").Favorite);
    }

    [Fact]
    public void The_favourite_filter_combines_with_query()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        var first = store.Append("alpha report", null, Monday);
        store.Append("alpha memo", null, Tuesday);
        store.Append("beta report", null, Tuesday);

        store.SetFavorite(first.Id, true);

        var found = store.Find(new HistoryFilter { Favorite = true, Query = "alpha" }, 10);

        Assert.Equal(["alpha report"], found.Select(e => e.Text));
    }

    [Fact]
    public void Notes_can_be_written_rewritten_and_removed()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        var entry = store.Append("0x1F6FEB", null, Monday);

        store.SetNote(entry.Id, "给客户的品牌蓝");
        Assert.Equal("给客户的品牌蓝", store.Page(10)[0].Note);

        store.SetNote(entry.Id, "品牌主色");
        Assert.Equal("品牌主色", store.Page(10)[0].Note);

        store.SetNote(entry.Id, null);
        Assert.Null(store.Page(10)[0].Note);
    }

    [Fact]
    public void Searching_finds_words_that_live_only_in_the_note()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        store.Append("0x1F6FEB", null, Monday);
        var entry = store.Append("#4C8DFF", null, Tuesday);
        store.SetNote(entry.Id, "夜间的品牌蓝");

        var found = store.Find(new HistoryFilter { Query = "夜间" }, 10);

        Assert.Equal(["#4C8DFF"], found.Select(e => e.Text));
    }

    [Fact]
    public void Usage_counts_only_go_up()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        var entry = store.Append("paste me", null, Monday);

        store.BumpUse(entry.Id);
        store.BumpUse(entry.Id);

        Assert.Equal(2, store.Page(10)[0].UseCount);
    }

    [Fact]
    public void Favourite_pin_and_note_coexist()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        var pinned = store.Append("everything at once", null, Monday);
        store.SetPinned(pinned.Id, true);
        store.SetFavorite(pinned.Id, true);
        store.SetNote(pinned.Id, "全都要");

        var entry = store.Page(10)[0];
        Assert.True(entry.IsPinned);
        Assert.True(entry.Favorite);
        Assert.Equal("全都要", entry.Note);
    }
}
