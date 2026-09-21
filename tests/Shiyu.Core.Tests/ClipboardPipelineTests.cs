using Shiyu.Core;
using Shiyu.Core.Tests.Fakes;

namespace Shiyu.Core.Tests;

public class ClipboardPipelineTests
{
    [Fact]
    public void Copying_text_creates_one_entry()
    {
        using var database = new TempDatabase();
        var clipboard = new FakeClipboardMonitor();
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(clipboard, store, TimeProvider.System, new ExclusionPolicy());

        clipboard.Emit("hello", sourceApp: "notepad");

        var entry = Assert.Single(store.Recent(limit: 10));
        Assert.Equal("hello", entry.Text);
        Assert.Equal("notepad", entry.SourceApp);
    }
}

public class ClipboardRecordingTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entry_records_when_it_was_copied()
    {
        using var database = new TempDatabase();
        var clipboard = new FakeClipboardMonitor();
        var clock = new TestClock(Noon);
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(clipboard, store, clock, new ExclusionPolicy());

        clipboard.Emit("hello");

        var entry = Assert.Single(store.Recent(limit: 10));
        Assert.Equal(Noon, entry.CreatedAt);
    }

    [Fact]
    public void History_survives_a_restart()
    {
        using var database = new TempDatabase();

        using (var store = EntryStore.Open(database.FilePath))
        {
            var clipboard = new FakeClipboardMonitor();
            using var pipeline = new ClipboardPipeline(clipboard, store, new TestClock(Noon), new ExclusionPolicy());
            clipboard.Emit("written before the restart");
        }

        using var reopened = EntryStore.Open(database.FilePath);

        var entry = Assert.Single(reopened.Recent(limit: 10));
        Assert.Equal("written before the restart", entry.Text);
    }

    [Fact]
    public void Each_distinct_copy_creates_its_own_entry_newest_first()
    {
        using var database = new TempDatabase();
        var clipboard = new FakeClipboardMonitor();
        var clock = new TestClock(Noon);
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(clipboard, store, clock, new ExclusionPolicy());

        foreach (var text in new[] { "first", "second", "third" })
        {
            clipboard.Emit(text);
            clock.Advance(TimeSpan.FromMilliseconds(5));
        }

        Assert.Equal(
            new[] { "third", "second", "first" },
            store.Recent(limit: 10).Select(entry => entry.Text));
    }

    [Fact]
    public void Copies_arriving_in_the_same_millisecond_all_survive()
    {
        using var database = new TempDatabase();
        var clipboard = new FakeClipboardMonitor();
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(clipboard, store, new TestClock(Noon), new ExclusionPolicy());

        // The clock never advances: bursts of copies must not collapse just
        // because two of them share a timestamp.
        foreach (var text in new[] { "a", "b", "c", "d", "e" })
        {
            clipboard.Emit(text);
        }

        Assert.Equal(5, store.Recent(limit: 10).Count);
    }

    [Fact]
    public void Copying_the_same_text_twice_in_a_row_refreshes_rather_than_duplicates()
    {
        using var database = new TempDatabase();
        var clipboard = new FakeClipboardMonitor();
        var clock = new TestClock(Noon);
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(clipboard, store, clock, new ExclusionPolicy());

        clipboard.Emit("same text");
        clock.Advance(TimeSpan.FromMinutes(1));
        clipboard.Emit("same text");

        var entry = Assert.Single(store.Recent(limit: 10));
        Assert.Equal(Noon.AddMinutes(1), entry.CreatedAt);
    }

    [Fact]
    public void Copying_the_same_text_again_after_something_else_creates_a_new_entry()
    {
        using var database = new TempDatabase();
        var clipboard = new FakeClipboardMonitor();
        var clock = new TestClock(Noon);
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(clipboard, store, clock, new ExclusionPolicy());

        foreach (var text in new[] { "alpha", "beta", "alpha" })
        {
            clipboard.Emit(text);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(
            new[] { "alpha", "beta", "alpha" },
            store.Recent(limit: 10).Select(entry => entry.Text));
    }
}
