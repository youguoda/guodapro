using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class RetentionTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path_ = Path.Combine(Path.GetTempPath(), "shiyu-retention", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path_);
        }

        public string Path_ { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path_, recursive: true); }
            catch (IOException) { }
        }
    }

    private static Entry SaveImage(EntryStore store, ImageArchive archive, DateTimeOffset at)
    {
        var path = archive.Save([1, 2, 3], at);
        return store.AppendImage($"图片 {at:HHmmss}", [9], path, "test", at);
    }

    [Fact]
    public void Originals_past_the_retention_period_are_deleted()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        using var store = EntryStore.Open(database.FilePath);
        var archive = new ImageArchive(folder.Path_);

        var old = SaveImage(store, archive, Noon.AddDays(-40));

        var result = new RetentionService(store, archive, new TestClock(Noon)).Sweep(TimeSpan.FromDays(30));

        Assert.Equal(1, result.Removed);
        Assert.False(File.Exists(old.OriginalPath!));
    }

    [Fact]
    public void The_entry_and_its_thumbnail_survive_the_cleanup()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        using var store = EntryStore.Open(database.FilePath);
        var archive = new ImageArchive(folder.Path_);
        SaveImage(store, archive, Noon.AddDays(-40));

        new RetentionService(store, archive, new TestClock(Noon)).Sweep(TimeSpan.FromDays(30));

        // This is the whole reason thumbnails live in the database: the history
        // stays complete and explicable after the originals are gone. The list
        // row is narrow (O-22); the payload read goes through Get.
        var entry = Assert.Single(store.Page(limit: 10));
        Assert.Equal(EntryKind.Image, entry.Kind);
        Assert.Equal(new byte[] { 9 }, store.Get(entry.Id)!.ThumbnailPng);
        Assert.False(entry.HasOriginal);
        Assert.Null(entry.OriginalPath);
    }

    [Fact]
    public void Recent_originals_are_left_alone()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        using var store = EntryStore.Open(database.FilePath);
        var archive = new ImageArchive(folder.Path_);
        var recent = SaveImage(store, archive, Noon.AddDays(-3));

        var result = new RetentionService(store, archive, new TestClock(Noon)).Sweep(TimeSpan.FromDays(30));

        Assert.Equal(0, result.Total);
        Assert.True(File.Exists(recent.OriginalPath!));
    }

    [Fact]
    public void Text_entries_are_never_cleaned_up_however_old()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        using var store = EntryStore.Open(database.FilePath);
        var archive = new ImageArchive(folder.Path_);
        store.Append("something copied years ago", "test", Noon.AddYears(-3));

        new RetentionService(store, archive, new TestClock(Noon)).Sweep(TimeSpan.FromDays(30));

        // Text is tiny and is exactly what the user goes looking for months
        // later. Retention has no business touching it.
        var entry = Assert.Single(store.Page(limit: 10));
        Assert.Equal("something copied years ago", entry.Text);
    }

    [Fact]
    public void Shortening_the_retention_period_applies_to_what_is_already_stored()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        using var store = EntryStore.Open(database.FilePath);
        var archive = new ImageArchive(folder.Path_);
        var image = SaveImage(store, archive, Noon.AddDays(-10));
        var service = new RetentionService(store, archive, new TestClock(Noon));

        Assert.Equal(0, service.Sweep(TimeSpan.FromDays(30)).Total);
        Assert.True(File.Exists(image.OriginalPath!));

        Assert.Equal(1, service.Sweep(TimeSpan.FromDays(7)).Removed);
        Assert.False(File.Exists(image.OriginalPath!));
    }

    [Fact]
    public void A_long_unused_installation_clears_its_whole_backlog()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        using var store = EntryStore.Open(database.FilePath);
        var archive = new ImageArchive(folder.Path_);

        // More than one batch, so the loop that works through a backlog is
        // actually exercised rather than assumed.
        for (var i = 0; i < 250; i++)
        {
            SaveImage(store, archive, Noon.AddDays(-100).AddSeconds(i));
        }

        var result = new RetentionService(store, archive, new TestClock(Noon)).Sweep(TimeSpan.FromDays(30));

        Assert.Equal(250, result.Removed);
        Assert.Empty(Directory.GetFiles(folder.Path_));
        Assert.Equal(250, store.Count());
    }

    [Fact]
    public void An_interrupted_sweep_leaves_no_entry_pointing_at_a_missing_file()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        using var store = EntryStore.Open(database.FilePath);
        var archive = new ImageArchive(folder.Path_);
        var image = SaveImage(store, archive, Noon.AddDays(-40));

        // Exactly the state a crash between the two steps would leave behind:
        // the file gone, the entry still claiming to have it.
        File.Delete(image.OriginalPath!);

        var result = new RetentionService(store, archive, new TestClock(Noon)).Sweep(TimeSpan.FromDays(30));

        Assert.Equal(1, result.Reclaimed);
        Assert.Null(Assert.Single(store.Page(limit: 10)).OriginalPath);
    }

    [Fact]
    public void Sweeping_twice_is_harmless()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        using var store = EntryStore.Open(database.FilePath);
        var archive = new ImageArchive(folder.Path_);
        SaveImage(store, archive, Noon.AddDays(-40));
        var service = new RetentionService(store, archive, new TestClock(Noon));

        service.Sweep(TimeSpan.FromDays(30));
        var second = service.Sweep(TimeSpan.FromDays(30));

        Assert.Equal(0, second.Total);
        Assert.Equal(1, store.Count());
    }

    [Fact]
    public void An_empty_history_sweeps_to_nothing()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        using var store = EntryStore.Open(database.FilePath);

        var result = new RetentionService(store, new ImageArchive(folder.Path_), new TestClock(Noon))
            .Sweep(TimeSpan.FromDays(30));

        Assert.Equal(0, result.Total);
    }
}
