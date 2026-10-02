using Shiyu.Core;
using Shiyu.Core.Tests.Fakes;

namespace Shiyu.Core.Tests;

public class ImageEntryTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path_ = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "shiyu-images", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path_);
        }

        public string Path_ { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path_, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class FakeImage(int width, int height) : IClipboardImage
    {
        public Exception? Fails { get; init; }

        public Task<RenderedImage> RenderAsync(CancellationToken cancellation = default)
            => Fails is not null
                ? Task.FromException<RenderedImage>(Fails)
                : Task.FromResult(new RenderedImage(
                    FullPng: [1, 2, 3, 4, 5, 6, 7, 8],
                    ThumbnailPng: [9, 9, 9],
                    width,
                    height));
    }

    [Fact]
    public async Task A_copied_image_becomes_an_entry_with_a_thumbnail_and_a_file()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        var clipboard = new FakeClipboardMonitor();
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(
            clipboard, store, new TestClock(Noon), new ExclusionPolicy(), new ImageArchive(folder.Path_));

        clipboard.EmitImage(new FakeImage(1920, 1080));
        await pipeline.Idle;

        var entry = Assert.Single(store.Recent(limit: 10));
        Assert.Equal(EntryKind.Image, entry.Kind);

        // The thumbnail is a payload: lists are narrow by design (O-22), so
        // the assertion asks the store for the entry the way a reader of the
        // full row would.
        Assert.Equal(new byte[] { 9, 9, 9 }, store.Get(entry.Id)!.ThumbnailPng);
        Assert.Contains("1920×1080", entry.Text);
        Assert.True(entry.HasOriginal);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, File.ReadAllBytes(entry.OriginalPath!));
    }

    [Fact]
    public async Task An_image_that_cannot_be_saved_is_reported_rather_than_vanishing()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        var clipboard = new FakeClipboardMonitor();
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(
            clipboard, store, new TestClock(Noon), new ExclusionPolicy(), new ImageArchive(folder.Path_));

        var failures = new List<string>();
        pipeline.ImageFailed += failures.Add;

        clipboard.EmitImage(new FakeImage(100, 100) { Fails = new InvalidOperationException("decode failed") });
        await pipeline.Idle;

        // The user watched themselves copy it; silence would read as a bug.
        Assert.Single(failures);
        Assert.Equal(0, store.Count());
    }

    [Fact]
    public async Task Two_images_copied_in_quick_succession_are_both_recorded_in_order()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        var clipboard = new FakeClipboardMonitor();
        var clock = new TestClock(Noon);
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(
            clipboard, store, clock, new ExclusionPolicy(), new ImageArchive(folder.Path_));

        clipboard.EmitImage(new FakeImage(100, 100));
        clock.Advance(TimeSpan.FromSeconds(1));
        clipboard.EmitImage(new FakeImage(200, 200));
        await pipeline.Idle;

        var entries = store.Recent(limit: 10);
        Assert.Equal(2, entries.Count);
        Assert.Contains("200×200", entries[0].Text);
        Assert.Contains("100×100", entries[1].Text);
    }

    [Fact]
    public async Task An_excluded_image_is_never_written_to_disk_at_all()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        var clipboard = new FakeClipboardMonitor();
        using var store = EntryStore.Open(database.FilePath);
        using var pipeline = new ClipboardPipeline(
            clipboard, store, new TestClock(Noon), new ExclusionPolicy(), new ImageArchive(folder.Path_));

        clipboard.EmitImage(new FakeImage(100, 100), excluded: true);
        await pipeline.Idle;

        Assert.Equal(0, store.Count());
        Assert.Empty(Directory.GetFiles(folder.Path_));
    }

    [Fact]
    public void Clearing_an_original_leaves_the_entry_and_its_thumbnail_behind()
    {
        using var database = new TempDatabase();
        using var folder = new TempFolder();
        using var store = EntryStore.Open(database.FilePath);
        var archive = new ImageArchive(folder.Path_);

        var path = archive.Save([1, 2, 3], Noon);
        var saved = store.AppendImage("图片 800×600", [7, 7], path, "test", Noon);

        archive.Delete(path);
        store.ClearOriginal(saved.Id);

        var entry = Assert.Single(store.Recent(limit: 10));

        // This is what stops the history developing holes the user cannot
        // explain once retention starts removing old originals.
        Assert.Equal(EntryKind.Image, entry.Kind);
        Assert.Equal(new byte[] { 7, 7 }, store.Get(entry.Id)!.ThumbnailPng);
        Assert.False(entry.HasOriginal);
    }

    [Fact]
    public void Deleting_an_original_that_is_already_gone_counts_as_success()
    {
        using var folder = new TempFolder();
        var archive = new ImageArchive(folder.Path_);

        Assert.True(archive.Delete(Path.Combine(folder.Path_, "never-existed.png")));
    }

    [Fact]
    public void Two_images_saved_in_the_same_second_do_not_overwrite_each_other()
    {
        using var folder = new TempFolder();
        var archive = new ImageArchive(folder.Path_);

        var first = archive.Save([1], Noon);
        var second = archive.Save([2], Noon);

        Assert.NotEqual(first, second);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(first));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(second));
    }
}

public class SchemaMigrationTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_existing_text_history_survives_the_upgrade_to_image_support()
    {
        using var database = new TempDatabase();

        // A database as the first version of Shiyu left it: three columns and
        // no user_version stamp.
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
                VALUES ('written by an older version', 'notepad', 1758542400000);
                """;
            create.ExecuteNonQuery();
        }

        using var store = EntryStore.Open(database.FilePath);

        // Upgrading must not cost the user their history; an empty list after
        // an update, with no explanation, is the worst possible outcome.
        var entry = Assert.Single(store.Recent(limit: 10));
        Assert.Equal("written by an older version", entry.Text);
        Assert.Equal(EntryKind.Text, entry.Kind);
        Assert.Null(entry.ThumbnailPng);
    }

    [Fact]
    public void Opening_an_up_to_date_database_twice_changes_nothing()
    {
        using var database = new TempDatabase();

        using (var first = EntryStore.Open(database.FilePath))
        {
            first.Append("hello", "test", Noon);
        }

        using var second = EntryStore.Open(database.FilePath);

        Assert.Equal(1, second.Count());
    }
}
