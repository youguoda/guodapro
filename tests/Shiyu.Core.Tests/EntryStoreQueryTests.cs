using System.Diagnostics;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class EntryStoreQueryTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static EntryStore Seed(TempDatabase database, params string[] texts)
    {
        var store = EntryStore.Open(database.FilePath);
        var at = Noon;
        foreach (var text in texts)
        {
            store.Append(text, sourceApp: "test", at);
            at = at.AddSeconds(1);
        }

        return store;
    }

    [Fact]
    public void Search_finds_entries_containing_the_words()
    {
        using var database = new TempDatabase();
        using var store = Seed(database, "the quick brown fox", "an unrelated note", "another fox sighting");

        var found = store.Search("fox", limit: 10);

        Assert.Equal(
            new[] { "another fox sighting", "the quick brown fox" },
            found.Select(entry => entry.Text));
    }

    [Fact]
    public void Search_ignores_case()
    {
        using var database = new TempDatabase();
        using var store = Seed(database, "Deployment Checklist");

        Assert.Single(store.Search("deployment", limit: 10));
        Assert.Single(store.Search("DEPLOYMENT", limit: 10));
    }

    [Fact]
    public void Search_matches_inside_chinese_text()
    {
        using var database = new TempDatabase();
        using var store = Seed(database, "这是一段剪贴板历史记录", "无关的内容");

        var entry = Assert.Single(store.Search("剪贴板", limit: 10));
        Assert.Equal("这是一段剪贴板历史记录", entry.Text);
    }

    [Fact]
    public void Search_treats_wildcards_in_the_query_literally()
    {
        using var database = new TempDatabase();
        using var store = Seed(database, "100% complete", "nothing like it", "a_b");

        // Were these passed through to LIKE, "%" and "_" would match anything
        // and the search would quietly return the whole history.
        var percent = Assert.Single(store.Search("100%", limit: 10));
        Assert.Equal("100% complete", percent.Text);

        var underscore = Assert.Single(store.Search("a_b", limit: 10));
        Assert.Equal("a_b", underscore.Text);
    }

    [Fact]
    public void Search_returns_newest_first()
    {
        using var database = new TempDatabase();
        using var store = Seed(database, "note one", "note two", "note three");

        Assert.Equal(
            new[] { "note three", "note two", "note one" },
            store.Search("note", limit: 10).Select(entry => entry.Text));
    }

    [Fact]
    public void Search_with_no_matches_returns_nothing_rather_than_everything()
    {
        using var database = new TempDatabase();
        using var store = Seed(database, "alpha", "beta");

        Assert.Empty(store.Search("gamma", limit: 10));
    }

    [Fact]
    public void An_empty_query_returns_nothing_rather_than_the_whole_history()
    {
        using var database = new TempDatabase();
        using var store = Seed(database, "alpha", "beta");

        Assert.Empty(store.Search("   ", limit: 10));
    }

    [Fact]
    public void Paging_walks_the_history_without_asking_for_all_of_it()
    {
        using var database = new TempDatabase();
        using var store = Seed(database, "one", "two", "three", "four", "five");

        Assert.Equal(new[] { "five", "four" }, store.Page(limit: 2, offset: 0).Select(e => e.Text));
        Assert.Equal(new[] { "three", "two" }, store.Page(limit: 2, offset: 2).Select(e => e.Text));
        Assert.Equal(new[] { "one" }, store.Page(limit: 2, offset: 4).Select(e => e.Text));
    }

    [Fact]
    public void Deleting_one_entry_leaves_the_rest_alone()
    {
        using var database = new TempDatabase();
        using var store = Seed(database, "keep me", "delete me", "keep me too");
        var doomed = store.Search("delete me", limit: 1).Single();

        Assert.True(store.Delete(doomed.Id));

        Assert.Equal(2, store.Count());
        Assert.Empty(store.Search("delete me", limit: 10));
    }

    [Fact]
    public void Deleting_an_entry_that_is_already_gone_reports_that_rather_than_throwing()
    {
        using var database = new TempDatabase();
        using var store = Seed(database, "only entry");

        Assert.False(store.Delete(9999));
    }

    [Fact]
    public void Deleting_a_date_range_spares_everything_outside_it()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        store.Append("long ago", "test", Noon.AddDays(-10));
        store.Append("inside the range", "test", Noon.AddDays(-5));
        store.Append("also inside", "test", Noon.AddDays(-4));
        store.Append("recent", "test", Noon);

        var removed = store.DeleteCreatedBetween(Noon.AddDays(-6), Noon.AddDays(-3));

        Assert.Equal(2, removed);
        Assert.Equal(
            new[] { "recent", "long ago" },
            store.Page(limit: 10, offset: 0).Select(entry => entry.Text));
    }

    [Fact]
    public void Clearing_the_history_empties_it()
    {
        using var database = new TempDatabase();
        using var store = Seed(database, "one", "two", "three");

        Assert.Equal(3, store.DeleteAll());
        Assert.Equal(0, store.Count());
        Assert.Empty(store.Page(limit: 10, offset: 0));
    }

    [Fact]
    public void Search_stays_quick_and_stays_correct_on_a_large_history()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        const int size = 50_000;
        var at = Noon.AddDays(-30);
        store.AppendMany(Enumerable.Range(0, size).Select(index =>
        {
            // One needle, buried at the very beginning so a search that only
            // looks at recent entries cannot accidentally pass.
            var text = index == 0
                ? "这是埋在最底下的一根针 needle"
                : $"ordinary clipboard entry number {index} 普通条目";
            return new NewEntry(text, "test", at.AddSeconds(index));
        }));

        Assert.Equal(size, store.Count());

        var stopwatch = Stopwatch.StartNew();
        var found = store.Search("埋在最底下", limit: 50);
        stopwatch.Stop();

        Assert.Single(found);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 1000,
            $"searching {size} entries took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A_page_of_a_large_history_is_bounded_by_the_limit_it_was_given()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        var at = Noon;
        store.AppendMany(Enumerable.Range(0, 5_000)
            .Select(index => new NewEntry($"entry {index}", "test", at.AddSeconds(index))));

        // The history is never read wholesale: every read path takes a limit.
        Assert.Equal(25, store.Page(limit: 25, offset: 0).Count);
        Assert.Equal(25, store.Recent(limit: 25).Count);
    }
}
