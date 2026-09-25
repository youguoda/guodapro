using Shiyu.Core;
using Shiyu.Core.Tests.Fakes;

namespace Shiyu.Core.Tests;

/// <summary>
/// A file entry is a capped list of paths plus a label that tells the truth
/// about that cap — the truth part is the acceptance criterion.
/// </summary>
public class FileEntryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Copying_files_creates_one_file_entry()
    {
        using var database = new TempDatabase();
        var clipboard = new FakeClipboardMonitor();
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(clipboard, store, new TestClock(Noon), new ExclusionPolicy());

        clipboard.EmitFiles([@"C:\docs\a.pdf", @"C:\docs\b.txt"], "explorer");

        var entry = Assert.Single(store.Recent(limit: 10));
        Assert.Equal(EntryKind.Files, entry.Kind);
        Assert.Equal(2, entry.Files.Count);
        Assert.Equal(@"C:\docs\a.pdf", entry.Files[0]);
    }

    [Fact]
    public void The_same_file_copy_twice_touches_rather_than_duplicating()
    {
        using var database = new TempDatabase();
        var clipboard = new FakeClipboardMonitor();
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(clipboard, store, new TestClock(Noon), new ExclusionPolicy());

        clipboard.EmitFiles([@"C:\docs\a.pdf"], "explorer");
        clipboard.EmitFiles([@"C:\docs\a.pdf"], "explorer");

        Assert.Single(store.Recent(limit: 10));
    }

    [Fact]
    public void An_absurd_number_of_files_is_capped_and_the_label_says_so()
    {
        var many = Enumerable.Range(0, 200).Select(i => $@"C:\folder\file{i}.txt").ToArray();

        var kept = FileEntries.WithinCap(many, out var capped);

        Assert.Equal(FileEntries.Cap, kept.Count);
        Assert.True(capped);
        Assert.Contains("200", FileEntries.Label(many, capped));
    }

    [Fact]
    public void Labels_read_naturally_for_small_and_medium_copies()
    {
        Assert.Equal("a.txt", FileEntries.Label([@"C:\a.txt"], capped: false));
        Assert.Contains("a.txt", FileEntries.Label([@"C:\a.txt", @"C:\b.txt"], capped: false));
        Assert.Contains("2", FileEntries.Label([@"C:\a.txt", @"C:\b.txt"], capped: false));
    }

    [Fact]
    public void Round_trip_preserves_the_paths()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        store.AppendFiles([@"C:\one\x.log", @"C:\two\y.log"], "terminal", Noon);

        var entry = Assert.Single(store.Recent(limit: 10));
        Assert.Equal([@"C:\one\x.log", @"C:\two\y.log"], entry.Files);
    }
}
