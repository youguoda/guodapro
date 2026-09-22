using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class TagsAndPinningTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static EntryStore Seeded(TempDatabase database, out Entry first, out Entry second)
    {
        var store = EntryStore.Open(database.FilePath);
        first = store.Append("the older note", "test", Noon.AddMinutes(-5));
        second = store.Append("the newer note", "test", Noon);
        return store;
    }

    [Fact]
    public void A_tag_can_be_attached_and_read_back()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out _);

        store.AddTag(first.Id, "项目甲");

        Assert.Equal(new[] { "项目甲" }, store.TagsOf(first.Id));
    }

    [Fact]
    public void An_entry_can_carry_several_tags()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out _);

        store.AddTag(first.Id, "项目甲");
        store.AddTag(first.Id, "待办");

        Assert.Equal(new[] { "待办", "项目甲" }, store.TagsOf(first.Id));
    }

    [Fact]
    public void Tags_arrive_with_the_entries_rather_than_needing_a_query_each()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out _);
        store.AddTag(first.Id, "项目甲");
        store.AddTag(first.Id, "待办");

        var loaded = store.Page(limit: 10, offset: 0).Single(entry => entry.Id == first.Id);

        // A list of a hundred entries would otherwise be a hundred extra
        // queries just to show their labels.
        Assert.Equal(2, loaded.Tags.Count);
        Assert.Contains("项目甲", loaded.Tags);
    }

    [Fact]
    public void The_same_tag_added_twice_is_still_one_tag()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out _);

        store.AddTag(first.Id, "项目甲");
        store.AddTag(first.Id, "项目甲");

        Assert.Single(store.TagsOf(first.Id));
    }

    [Fact]
    public void Tags_differing_only_in_case_are_one_tag()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out var second);

        store.AddTag(first.Id, "Work");
        store.AddTag(second.Id, "work");

        // Two tags that look alike but behave differently would be a quiet
        // source of "why is this one not in the list".
        Assert.Single(store.AllTags());
    }

    [Fact]
    public void Surrounding_space_is_trimmed_and_blank_tags_are_refused()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out _);

        store.AddTag(first.Id, "  待办  ");
        store.AddTag(first.Id, "   ");

        Assert.Equal(new[] { "待办" }, store.TagsOf(first.Id));
    }

    [Fact]
    public void Removing_a_tag_from_one_entry_leaves_it_on_the_others()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out var second);
        store.AddTag(first.Id, "待办");
        store.AddTag(second.Id, "待办");

        store.RemoveTag(first.Id, "待办");

        Assert.Empty(store.TagsOf(first.Id));
        Assert.Equal(new[] { "待办" }, store.TagsOf(second.Id));
    }

    [Fact]
    public void Deleting_a_tag_everywhere_leaves_the_entries_themselves_alone()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out var second);
        store.AddTag(first.Id, "待办");
        store.AddTag(second.Id, "待办");

        store.DeleteTag("待办");

        // Losing content because a label was tidied away would be an
        // unpleasant surprise.
        Assert.Equal(2, store.Count());
        Assert.Empty(store.TagsOf(first.Id));
        Assert.Empty(store.AllTags());
    }

    [Fact]
    public void Deleting_an_entry_takes_its_tag_links_with_it()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out var second);
        store.AddTag(first.Id, "待办");
        store.AddTag(second.Id, "待办");

        store.Delete(first.Id);

        Assert.Empty(store.TagsOf(first.Id));
        Assert.Equal(new[] { "待办" }, store.TagsOf(second.Id));
    }

    [Fact]
    public void A_pinned_entry_comes_first_even_when_it_is_the_older_one()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out _);

        store.SetPinned(first.Id, pinned: true);

        var entries = store.Page(limit: 10, offset: 0);
        Assert.Equal("the older note", entries[0].Text);
        Assert.True(entries[0].IsPinned);
    }

    [Fact]
    public void Unpinning_returns_the_entry_to_its_place_in_time()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out _);
        store.SetPinned(first.Id, pinned: true);

        store.SetPinned(first.Id, pinned: false);

        Assert.Equal("the newer note", store.Page(limit: 10, offset: 0)[0].Text);
    }

    [Fact]
    public void Pinning_survives_a_restart()
    {
        using var database = new TempDatabase();
        long pinnedId;

        using (var store = Seeded(database, out var first, out _))
        {
            store.SetPinned(first.Id, pinned: true);
            pinnedId = first.Id;
        }

        using var reopened = EntryStore.Open(database.FilePath);

        Assert.True(reopened.Page(limit: 10, offset: 0).Single(e => e.Id == pinnedId).IsPinned);
    }

    [Fact]
    public void Pinned_entries_lead_a_search_too()
    {
        using var database = new TempDatabase();
        using var store = Seeded(database, out var first, out _);
        store.SetPinned(first.Id, pinned: true);

        var found = store.Find(new HistoryFilter { Query = "note" }, limit: 10);

        Assert.Equal("the older note", found[0].Text);
    }

    [Fact]
    public void An_older_database_gains_tagging_without_losing_anything()
    {
        using var database = new TempDatabase();

        using (var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database.FilePath}"))
        {
            old.Open();
            using var create = old.CreateCommand();
            create.CommandText = """
                CREATE TABLE entries (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    text        TEXT    NOT NULL,
                    source_app  TEXT    NULL,
                    created_at  INTEGER NOT NULL
                );
                INSERT INTO entries (text, source_app, created_at)
                VALUES ('from two versions ago', 'notepad', 1758542400000);
                """;
            create.ExecuteNonQuery();
        }

        using var store = EntryStore.Open(database.FilePath);
        var entry = Assert.Single(store.Page(limit: 10, offset: 0));

        Assert.Equal("from two versions ago", entry.Text);
        Assert.False(entry.IsPinned);
        Assert.Empty(entry.Tags);

        store.AddTag(entry.Id, "补的标签");
        Assert.Equal(new[] { "补的标签" }, store.TagsOf(entry.Id));
    }
}

public class TagFilterTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Filtering_by_tag_returns_only_entries_carrying_it()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        var tagged = store.Append("tagged note", "test", Noon);
        store.Append("untagged note", "test", Noon.AddMinutes(1));
        store.AddTag(tagged.Id, "项目甲");

        var found = store.Find(new HistoryFilter { Tag = "项目甲" }, limit: 10);

        var entry = Assert.Single(found);
        Assert.Equal("tagged note", entry.Text);
    }

    [Fact]
    public void A_tag_filter_combines_with_a_keyword()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        var first = store.Append("deployment note", "test", Noon);
        var second = store.Append("unrelated note", "test", Noon.AddMinutes(1));
        store.AddTag(first.Id, "项目甲");
        store.AddTag(second.Id, "项目甲");

        var found = store.Find(new HistoryFilter { Tag = "项目甲", Query = "deployment" }, limit: 10);

        Assert.Single(found);
    }

    [Fact]
    public void A_tag_filter_counts_as_narrowing()
        => Assert.False(new HistoryFilter { Tag = "项目甲" }.IsEmpty);
}
