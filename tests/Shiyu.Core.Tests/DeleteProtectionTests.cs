using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// Protection is a promise with edges: favourites and pins survive the
/// deletes nobody reads row by row (retention, ranges, clear-all), the delete
/// entry points vanish rather than grey out, and the moment the marker is
/// gone the entry is deletable again — no dead ends.
/// </summary>
public class DeleteProtectionTests
{
    private static readonly DateTimeOffset Monday =
        new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Friday =
        new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);

    private sealed class Bench : IDisposable
    {
        public TempDatabase Database { get; } = new();
        public EntryStore Store { get; }

        public long Plain { get; }
        public long Starred { get; }
        public long Pinned { get; }

        public Bench()
        {
            Store = EntryStore.Open(Database.FilePath);
            Plain = Store.Append("plain note", "ZCode", Monday).Id;
            Starred = Store.Append("starred note", "ZCode", Tuesday()).Id;
            Store.SetFavorite(Starred, true);
            Pinned = Store.Append("pinned note", "ZCode", Wednesday()).Id;
            Store.SetPinned(Pinned, true);
        }

        private static DateTimeOffset Tuesday() => Monday.AddDays(1);
        private static DateTimeOffset Wednesday() => Monday.AddDays(2);

        public void Dispose()
        {
            Store.Dispose();
            Database.Dispose();
        }
    }

    [Fact]
    public void A_range_delete_sparves_favourites_and_pins_under_protection()
    {
        using var bench = new Bench();

        var removed = bench.Store.DeleteCreatedBetween(
            Monday, Friday, keepFavorites: true, keepPinned: true);

        Assert.Equal(1, removed);
        var survivors = bench.Store.Recent(limit: 10).Select(entry => entry.Id).ToList();
        Assert.Equal([bench.Pinned, bench.Starred], survivors);
    }

    [Fact]
    public void Without_protection_the_old_behaviour_stands()
    {
        using var bench = new Bench();

        var removed = bench.Store.DeleteCreatedBetween(Monday, Friday);

        Assert.Equal(3, removed);
        Assert.Equal(0, bench.Store.Count());
    }

    [Fact]
    public void Either_switch_alone_sparves_only_its_own()
    {
        using var bench = new Bench();

        bench.Store.DeleteCreatedBetween(Monday, Friday, keepFavorites: true, keepPinned: false);

        var left = bench.Store.Recent(limit: 10).Select(entry => entry.Id).ToList();
        Assert.Equal([bench.Starred], left);
    }

    [Fact]
    public void Clear_all_keeps_the_protected_and_says_how_many()
    {
        using var bench = new Bench();

        Assert.Equal(2, bench.Store.CountProtected(keepFavorites: true, keepPinned: true));
        var removed = bench.Store.DeleteAll(keepFavorites: true, keepPinned: true);

        Assert.Equal(1, removed);
        Assert.Equal(2, bench.Store.Count());
        Assert.DoesNotContain(bench.Store.Recent(limit: 10), entry => entry.Id == bench.Plain);
    }

    [Fact]
    public void Un_starring_returns_the_entry_to_the_ordinary_rules()
    {
        using var bench = new Bench();
        bench.Store.SetFavorite(bench.Starred, false);

        bench.Store.DeleteCreatedBetween(Monday, Friday, keepFavorites: true, keepPinned: true);

        Assert.Single(bench.Store.Recent(limit: 10));
        Assert.Equal(bench.Pinned, bench.Store.Recent(limit: 10)[0].Id);
    }

    [Fact]
    public void Retention_sweeps_spare_protected_originals()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        var images = Path.Combine(Path.GetTempPath(), "shiyu-protect-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(images);
        try
        {
            var old = Monday.AddDays(-60);
            var spare = store.AppendImage("图 1", [1, 2, 3], WriteOriginal(images, "spare.png"), "Weixin", old);
            store.SetFavorite(spare.Id, true);
            var gone = store.AppendImage("图 2", [4, 5, 6], WriteOriginal(images, "gone.png"), "Weixin", old);

            var clock = new TestClock(Friday);
            var archive = new ImageArchive(images);
            var swept = new RetentionService(store, archive, clock)
                .Sweep(TimeSpan.FromDays(30), keepFavorites: true, keepPinned: true);

            Assert.Equal(1, swept.Total);
            Assert.True(File.Exists(Path.Combine(images, "spare.png")));
            Assert.False(File.Exists(Path.Combine(images, "gone.png")));
        }
        finally
        {
            Directory.Delete(images, recursive: true);
        }
    }

    [Fact]
    public void The_tray_hides_delete_for_a_protected_entry()
    {
        var chosen = new[] { "copy", "delete", "pin", "favorite" };

        var plain = HoverActions.AvailableFor(chosen, EntryKind.Text, hasOriginal: false, deleteProtected: false);
        var guarded = HoverActions.AvailableFor(chosen, EntryKind.Text, hasOriginal: false, deleteProtected: true);

        Assert.Contains("delete", plain);
        Assert.DoesNotContain("delete", guarded);
    }

    [Fact]
    public void Protection_is_on_by_default_in_settings()
    {
        Assert.True(new AppSettings().ProtectFavorites);
        Assert.True(new AppSettings().ProtectPinned);
    }

    [Fact]
    public void The_range_count_for_the_confirmation_counts_only_protected()
    {
        using var bench = new Bench();

        Assert.Equal(2, bench.Store.CountProtectedBetween(
            Monday, Friday, keepFavorites: true, keepPinned: true));
        Assert.Equal(1, bench.Store.CountProtectedBetween(
            Monday, Monday.AddDays(1), keepFavorites: true, keepPinned: true));
    }

    private static string WriteOriginal(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, [9, 9, 9]);
        return path;
    }
}
