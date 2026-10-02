using Shiyu.Core;
using Shiyu.Core.Tests.Fakes;

namespace Shiyu.Core.Tests;

/// <summary>
/// The formatted forms of a rich copy: kept when sane, dropped when enormous,
/// never costing the entry, and never turning one copy into two rows.
/// </summary>
public class FormattedEntryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Html_and_rtf_survive_the_round_trip()
    {
        using var database = new TempDatabase();
        var clipboard = new FakeClipboardMonitor();
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(clipboard, store, new TestClock(Noon), new ExclusionPolicy());

        clipboard.Emit("formatted text", html: "<p>formatted <b>text</b></p>", rtf: @"{\rtf1 hi}");

        var listed = Assert.Single(store.Recent(limit: 10));
        var entry = store.Get(listed.Id)!;
        Assert.Equal("<p>formatted <b>text</b></p>", entry.Html);
        Assert.Equal(@"{\rtf1 hi}", entry.Rtf);
    }

    [Fact]
    public void An_enormous_formatted_form_is_dropped_but_the_entry_stays()
    {
        using var database = new TempDatabase();
        var clipboard = new FakeClipboardMonitor();
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(clipboard, store, new TestClock(Noon), new ExclusionPolicy());

        clipboard.Emit("whole page", html: "<p>" + new string('x', 600_000) + "</p>");

        var listed = Assert.Single(store.Recent(limit: 10));
        Assert.Equal("whole page", listed.Text);
        Assert.Null(store.Get(listed.Id)!.Html);
    }

    [Fact]
    public void Writing_a_rich_entry_back_does_not_create_a_second_row()
    {
        using var database = new TempDatabase();
        var clipboard = new FakeClipboardMonitor();
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(clipboard, store, new TestClock(Noon), new ExclusionPolicy());

        clipboard.Emit("same text", html: "<p>same text</p>");

        // The multi-format write-back arrives as one notification carrying
        // the same text and the same formats again.
        clipboard.Emit("same text", html: "<p>same text</p>");

        Assert.Single(store.Recent(limit: 10));
    }
}
