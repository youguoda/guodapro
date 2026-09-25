using Shiyu.Core;
using Shiyu.Core.Tests.Fakes;

namespace Shiyu.Core.Tests;

/// <summary>
/// Source-app icons are an enhancement, never a dependency: recording must
/// survive icon failures, one row per application must stand between a
/// thousand entries and a thousand icon copies, and an application whose icon
/// could not be found must not be re-asked on every copy.
/// </summary>
public class SourceIconTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeIconProvider : ISourceIconProvider
    {
        public int Calls;

        public List<string> AskedPaths = [];

        public Func<string, byte[]?> OnExtract { get; set; } = _ => [1, 2, 3];

        public byte[]? ExtractIconPng(string exePath)
        {
            Calls++;
            AskedPaths.Add(exePath);
            return OnExtract(exePath);
        }
    }

    private static (FakeClipboardMonitor Clipboard, EntryStore Store, ClipboardPipeline Pipeline, FakeIconProvider Icons)
        NewPipeline(TempDatabase database, FakeIconProvider? icons = null)
    {
        var clipboard = new FakeClipboardMonitor();
        var store = EntryStore.Open(database.FilePath);
        icons ??= new FakeIconProvider();
        var pipeline = new ClipboardPipeline(
            clipboard, store, new TestClock(Noon), new ExclusionPolicy(), icons: new SourceIconCache(store, icons));

        return (clipboard, store, pipeline, icons);
    }

    [Fact]
    public void The_first_copy_from_an_app_extracts_and_stores_its_icon()
    {
        using var database = new TempDatabase();
        var (clipboard, store, pipeline, icons) = NewPipeline(database);

        clipboard.Emit("hello", sourceApp: "notepad", sourceExePath: @"C:\Windows\notepad.exe");

        Assert.Equal(1, icons.Calls);
        Assert.True(store.HasApplicationIcon("notepad"));
        Assert.NotNull(store.ApplicationIcon("notepad"));
        pipeline.Dispose();
        store.Dispose();
    }

    [Fact]
    public void A_thousand_copies_from_one_app_ask_for_the_icon_once()
    {
        using var database = new TempDatabase();
        var (clipboard, store, pipeline, icons) = NewPipeline(database);

        clipboard.Emit("first", sourceApp: "notepad", sourceExePath: @"C:\Windows\notepad.exe");
        clipboard.Emit("second", sourceApp: "notepad", sourceExePath: @"C:\Windows\notepad.exe");
        clipboard.Emit("third", sourceApp: "notepad", sourceExePath: @"C:\Windows\notepad.exe");

        Assert.Equal(1, icons.Calls);
        Assert.Equal(3, store.Recent(limit: 10).Count);
        pipeline.Dispose();
        store.Dispose();
    }

    [Fact]
    public void An_app_without_a_findable_icon_is_not_re_asked()
    {
        using var database = new TempDatabase();
        var icons = new FakeIconProvider { OnExtract = _ => null };
        var (clipboard, store, pipeline, provider) = NewPipeline(database, icons);

        clipboard.Emit("hello", sourceApp: "ghost", sourceExePath: @"C:\gone\ghost.exe");
        clipboard.Emit("again", sourceApp: "ghost", sourceExePath: @"C:\gone\ghost.exe");

        // The tombstone (a stored row with no icon) is what stops the retry.
        Assert.Equal(1, provider.Calls);
        Assert.True(store.HasApplicationIcon("ghost"));
        Assert.Null(store.ApplicationIcon("ghost"));
        pipeline.Dispose();
        store.Dispose();
    }

    [Fact]
    public void A_throwing_icon_provider_costs_the_icon_not_the_entry()
    {
        using var database = new TempDatabase();
        var icons = new FakeIconProvider { OnExtract = _ => throw new IOException("icon backend broke") };
        var (clipboard, store, pipeline, _) = NewPipeline(database, icons);

        clipboard.Emit("must survive", sourceApp: "fragile", sourceExePath: @"C:\fragile.exe");

        Assert.Single(store.Recent(limit: 10), entry => entry.Text == "must survive");
        pipeline.Dispose();
        store.Dispose();
    }

    [Fact]
    public void Copies_without_a_known_executable_never_ask_for_an_icon()
    {
        using var database = new TempDatabase();
        var (clipboard, store, pipeline, icons) = NewPipeline(database);

        clipboard.Emit("no app at all");
        clipboard.Emit("app but no path", sourceApp: "mystery");

        Assert.Equal(0, icons.Calls);
        Assert.Equal(2, store.Recent(limit: 10).Count);
        pipeline.Dispose();
        store.Dispose();
    }

    [Fact]
    public void Image_copies_get_their_source_icon_too()
    {
        using var database = new TempDatabase();
        var (clipboard, store, pipeline, icons) = NewPipeline(database);

        clipboard.EmitImage("image 8×8", sourceApp: "paint", sourceExePath: @"C:\paint.exe");

        Assert.Equal(1, icons.Calls);
        Assert.True(store.HasApplicationIcon("paint"));
        pipeline.Dispose();
        store.Dispose();
    }

    [Fact]
    public void Cached_icons_survive_a_restart()
    {
        using var database = new TempDatabase();

        using (var store = EntryStore.Open(database.FilePath))
        {
            store.SaveApplicationIcon("notepad", [9, 9, 9]);
        }

        using (var reopened = EntryStore.Open(database.FilePath))
        {
            // The application is gone from the machine, but what was cached
            // while it lived is what the history shows.
            Assert.Equal([9, 9, 9], reopened.ApplicationIcon("notepad"));
        }
    }

    [Fact]
    public void Excluded_content_never_touches_the_icon_store()
    {
        using var database = new TempDatabase();
        var (clipboard, store, pipeline, icons) = NewPipeline(database);

        clipboard.Emit("secret", sourceApp: "vault", sourceExePath: @"C:\vault.exe", excluded: true);

        Assert.Equal(0, icons.Calls);
        Assert.False(store.HasApplicationIcon("vault"));
        pipeline.Dispose();
        store.Dispose();
    }
}
