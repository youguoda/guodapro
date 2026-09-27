using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// Groups are exclusive containment: filing an entry says which pile it
/// belongs to, deleting the pile must not take the entry with it, and the
/// group filter has to combine with everything else the user narrows by.
/// </summary>
public class EntryGroupTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private sealed class Bench : IDisposable
    {
        public TempDatabase Database { get; } = new();
        public EntryStore Store { get; }

        public Bench()
        {
            Store = EntryStore.Open(Database.FilePath);
            Alpha = Store.Append("alpha content", "ZCode", Noon).Id;
            Beta = Store.Append("beta content", "ZCode", Noon.AddMinutes(1)).Id;
        }

        public long Alpha { get; }
        public long Beta { get; }

        public void Dispose()
        {
            Store.Dispose();
            Database.Dispose();
        }
    }

    [Fact]
    public void Groups_list_in_creation_order_with_names_and_icons()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        var first = store.CreateGroup("项目甲", "甲");
        store.CreateGroup("日常", "日");

        var groups = store.Groups();
        Assert.Equal(2, groups.Count);
        Assert.Equal(first, groups[0].Id);
        Assert.Equal("项目甲", groups[0].Name);
        Assert.Equal("甲", groups[0].Icon);
        Assert.Equal("日常", groups[1].Name);
        Assert.Equal(1, groups[0].Position);
        Assert.Equal(2, groups[1].Position);
    }

    [Fact]
    public void Filing_an_entry_into_a_group_and_filtering_finds_only_it()
    {
        using var bench = new Bench();
        var group = bench.Store.CreateGroup("项目甲", "甲");

        bench.Store.SetEntryGroup(bench.Alpha, group);

        var filed = bench.Store.Find(new HistoryFilter { Group = group }, limit: 10);
        Assert.Equal(bench.Alpha, Assert.Single(filed).Id);
        Assert.Equal(group, filed[0].GroupId);
    }

    [Fact]
    public void Filing_elsewhere_moves_the_entry_rather_than_duplicating_it()
    {
        using var bench = new Bench();
        var first = bench.Store.CreateGroup("项目甲", "甲");
        var second = bench.Store.CreateGroup("日常", "日");

        bench.Store.SetEntryGroup(bench.Alpha, first);
        bench.Store.SetEntryGroup(bench.Alpha, second);

        Assert.Empty(bench.Store.Find(new HistoryFilter { Group = first }, limit: 10));
        Assert.Equal(bench.Alpha, Assert.Single(bench.Store.Find(new HistoryFilter { Group = second }, limit: 10)).Id);
    }

    [Fact]
    public void Removing_from_a_group_returns_the_entry_to_ungrouped()
    {
        using var bench = new Bench();
        var group = bench.Store.CreateGroup("项目甲", "甲");

        bench.Store.SetEntryGroup(bench.Alpha, group);
        bench.Store.SetEntryGroup(bench.Alpha, null);

        Assert.Empty(bench.Store.Find(new HistoryFilter { Group = group }, limit: 10));
        Assert.Null(bench.Store.Recent(limit: 10).Single(entry => entry.Id == bench.Alpha).GroupId);
    }

    [Fact]
    public void Deleting_a_group_keeps_its_entries_as_ungrouped()
    {
        using var bench = new Bench();
        var group = bench.Store.CreateGroup("项目甲", "甲");
        bench.Store.SetEntryGroup(bench.Alpha, group);

        bench.Store.DeleteGroup(group);

        Assert.Empty(bench.Store.Groups());
        Assert.Empty(bench.Store.Find(new HistoryFilter { Group = group }, limit: 10));
        Assert.Equal(2, bench.Store.Count());
        Assert.All(bench.Store.Recent(limit: 10), entry => Assert.Null(entry.GroupId));
        Assert.Equal(bench.Beta, bench.Store.Recent(limit: 10)[0].Id);
    }

    [Fact]
    public void The_group_filter_combines_with_keyword_and_kind()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        var filed = store.Append("alpha report", "ZCode", Noon);
        store.Append("alpha memo", "ZCode", Noon.AddMinutes(1));
        var group = store.CreateGroup("项目甲", "甲");
        store.SetEntryGroup(filed.Id, group);

        var narrowed = store.Find(
            new HistoryFilter { Group = group, Kind = EntryKind.Text, Query = "report" }, limit: 10);
        Assert.Equal(filed.Id, Assert.Single(narrowed).Id);

        // The group applies to its members only, whatever else is asked.
        var byKeyword = store.Find(new HistoryFilter { Group = group, Query = "memo" }, limit: 10);
        Assert.Empty(byKeyword);
    }

    [Fact]
    public void Moving_a_group_swaps_its_place_in_the_listing()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        var first = store.CreateGroup("项目甲", "甲");
        store.CreateGroup("日常", "日");
        var third = store.CreateGroup("归档", "档");

        store.MoveGroup(third, -1);

        var names = store.Groups().Select(group => group.Name).ToList();
        Assert.Equal(["项目甲", "归档", "日常"], names);
        Assert.Equal(first, store.Groups()[0].Id);
    }

    [Fact]
    public void Moving_past_either_end_is_a_no_op()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        var first = store.CreateGroup("项目甲", "甲");

        store.MoveGroup(first, -1);

        Assert.Equal("项目甲", Assert.Single(store.Groups()).Name);
    }

    [Fact]
    public void Hiding_keeps_the_group_assignable_but_flags_it()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        var group = store.CreateGroup("项目甲", "甲");

        store.SetGroupHidden(group, true);

        Assert.True(Assert.Single(store.Groups()).Hidden);
    }

    [Fact]
    public void Counting_a_groups_members_reads_the_pile_before_deleting_it()
    {
        // 删除确认的判据（票 39）：任何非空分组删除都确认。非空与否由
        // 库里数出来，不是抽屉当前恰好是不是它。
        using var bench = new Bench();
        var group = bench.Store.CreateGroup("项目甲", "甲");
        var other = bench.Store.CreateGroup("日常", "日");

        Assert.Equal(0, bench.Store.GroupEntryCount(group));

        bench.Store.SetEntryGroup(bench.Alpha, group);

        Assert.Equal(1, bench.Store.GroupEntryCount(group));
        Assert.Equal(0, bench.Store.GroupEntryCount(other));
    }

    [Fact]
    public void Renaming_and_reiconning_update_the_listing()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        var group = store.CreateGroup("项目甲", "甲");

        store.RenameGroup(group, "项目乙");
        store.SetGroupIcon(group, "乙");

        var updated = Assert.Single(store.Groups());
        Assert.Equal("项目乙", updated.Name);
        Assert.Equal("乙", updated.Icon);
    }
}
