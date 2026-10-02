using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class HistoryBrowserTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static EntryStore SeedNumbered(TempDatabase database, int count, string prefix = "entry")
    {
        var store = EntryStore.Open(database.FilePath);
        store.AppendMany(Enumerable.Range(0, count)
            .Select(index => new NewEntry($"{prefix} {index}", "test", Noon.AddSeconds(index))));
        return store;
    }

    [Fact]
    public void It_reads_one_page_at_a_time_until_the_history_runs_out()
    {
        using var database = new TempDatabase();
        using var store = SeedNumbered(database, 25);
        var browser = new HistoryBrowser(store, pageSize: 10);

        browser.Reset();
        Assert.Equal(10, browser.Loaded.Count);
        Assert.True(browser.HasMore);

        browser.LoadMore();
        Assert.Equal(20, browser.Loaded.Count);

        browser.LoadMore();
        Assert.Equal(25, browser.Loaded.Count);
        Assert.False(browser.HasMore);

        Assert.Equal(0, browser.LoadMore());
    }

    [Fact]
    public void Pages_do_not_repeat_or_skip_entries()
    {
        using var database = new TempDatabase();
        using var store = SeedNumbered(database, 55);
        var browser = new HistoryBrowser(store, pageSize: 10);

        browser.Reset();
        while (browser.HasMore)
        {
            browser.LoadMore();
        }

        Assert.Equal(55, browser.Loaded.Count);
        Assert.Equal(55, browser.Loaded.Select(entry => entry.Id).Distinct().Count());
    }

    [Fact]
    public void Searching_starts_again_from_the_top()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        store.AppendMany(
        [
            new NewEntry("a deployment note", "test", Noon),
            new NewEntry("something else", "test", Noon.AddSeconds(1)),
            new NewEntry("another deployment note", "test", Noon.AddSeconds(2)),
        ]);

        var browser = new HistoryBrowser(store, pageSize: 10);
        browser.Reset();
        Assert.Equal(3, browser.Loaded.Count);

        browser.Query = "deployment";

        Assert.Equal(2, browser.Loaded.Count);
        Assert.All(browser.Loaded, entry => Assert.Contains("deployment", entry.Text));
    }

    [Fact]
    public void Clearing_the_search_returns_to_the_whole_history()
    {
        using var database = new TempDatabase();
        using var store = SeedNumbered(database, 5);
        var browser = new HistoryBrowser(store, pageSize: 10);

        browser.Query = "entry 3";
        Assert.Single(browser.Loaded);

        browser.Query = string.Empty;
        Assert.Equal(5, browser.Loaded.Count);
    }

    [Fact]
    public void A_search_pages_too()
    {
        using var database = new TempDatabase();
        using var store = SeedNumbered(database, 30, prefix: "match");
        var browser = new HistoryBrowser(store, pageSize: 10);

        browser.Query = "match";
        Assert.Equal(10, browser.Loaded.Count);

        browser.LoadMore();
        browser.LoadMore();

        Assert.Equal(30, browser.Loaded.Count);
        Assert.Equal(30, browser.Loaded.Select(entry => entry.Id).Distinct().Count());
    }

    [Fact]
    public void A_new_copy_arriving_mid_walk_neither_repeats_nor_skips_rows()
    {
        using var database = new TempDatabase();
        using var store = SeedNumbered(database, 25);
        var browser = new HistoryBrowser(store, pageSize: 10);

        browser.Reset();
        Assert.Equal(10, browser.Loaded.Count);

        // The copy that lands while the user is mid-scroll. An OFFSET window
        // would shift down by one and read some row twice; the cursor just
        // continues below where it left off, and the newcomer waits for the
        // next reset (O-22).
        store.Append("a brand new copy", "test", Noon.AddMinutes(1));

        browser.LoadMore();
        browser.LoadMore();

        Assert.Equal(25, browser.Loaded.Count);
        Assert.Equal(25, browser.Loaded.Select(entry => entry.Id).Distinct().Count());
        Assert.DoesNotContain(browser.Loaded, entry => entry.Text == "a brand new copy");
    }

    [Fact]
    public void Forgetting_a_deleted_entry_does_not_send_the_reader_back_to_the_top()
    {
        using var database = new TempDatabase();
        using var store = SeedNumbered(database, 30);
        var browser = new HistoryBrowser(store, pageSize: 10);

        browser.Reset();
        browser.LoadMore();
        var doomed = browser.Loaded[5];

        store.Delete(doomed.Id);
        browser.Forget(doomed.Id);

        Assert.Equal(19, browser.Loaded.Count);
        Assert.DoesNotContain(browser.Loaded, entry => entry.Id == doomed.Id);
    }

    [Fact]
    public void An_empty_history_reads_as_empty_rather_than_as_more_to_come()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        var browser = new HistoryBrowser(store, pageSize: 10);

        browser.Reset();

        Assert.Empty(browser.Loaded);
        Assert.False(browser.HasMore);
    }
}
