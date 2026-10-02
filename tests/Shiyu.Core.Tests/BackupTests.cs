using System.IO.Compression;
using System.Text;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// A backup is the promise that the history can leave this machine and come
/// back whole: every marker — tags, groups, favourites, notes, pins — has to
/// survive the round trip, a merge must not duplicate what is already there,
/// and a wrong password must fail loudly rather than restore garbage.
/// </summary>
public class BackupTests : IDisposable
{
    private readonly TempDatabase _database = new();

    private readonly string _images;

    private readonly string _file;

    public BackupTests()
    {
        _images = Path.Combine(Path.GetTempPath(), "shiyu-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_images);
        _file = Path.Combine(_images, "original.png");
        File.WriteAllBytes(_file, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_images, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private EntryStore SeededStore()
    {
        var store = EntryStore.Open(_database.FilePath);

        var group = store.CreateGroup("项目甲", "甲");
        var pinned = store.Append("pinned content", "ZCode", Noon);
        store.SetEntryGroup(pinned.Id, group);
        store.SetPinned(pinned.Id, true);
        store.SetNote(pinned.Id, "这是给未来的话");
        store.AddTag(pinned.Id, "待办");
        store.AddTag(pinned.Id, "项目");

        var image = store.AppendImage(
            "图片 8×8", Convert.FromBase64String("iVBORw0KGgo="), _file, "Weixin", Noon.AddMinutes(1));
        store.SetFavorite(image.Id, true);

        store.Append("plain text goes too", "explorer", Noon.AddMinutes(2));
        return store;
    }

    private static byte[] PasswordOf(string password)
        => Encoding.Unicode.GetBytes(password);

    [Fact]
    public void An_unencrypted_round_trip_restores_every_marker()
    {
        using var store = SeededStore();
        var backup = Path.Combine(_images, "plain.shiyubk");

        var counts = BackupArchive.Export(backup, store, _images, settingsJson: "{}", passwordUtf16: null);
        Assert.Equal(3, counts.Entries);

        store.ClearAll();
        Assert.Equal(0, store.Count());

        var outcome = BackupArchive.Import(
            backup, store, _images, overwrite: true, passwordUtf16: null);

        Assert.Equal(3, outcome.Added);
        Assert.Equal("{}", outcome.SettingsJson);
        var restored = store.Recent(limit: 10);
        Assert.Equal(3, restored.Count);

        var pinnedBack = restored.Single(entry => entry.Text == "pinned content");
        Assert.True(pinnedBack.IsPinned);
        Assert.Equal("这是给未来的话", pinnedBack.Note);
        Assert.Equal(["待办", "项目"], pinnedBack.Tags);
        Assert.Equal("项目甲", store.GroupOf(pinnedBack)!.Name);

        var imageBack = restored.Single(entry => entry.Kind == EntryKind.Image);
        Assert.True(imageBack.Favorite);
        Assert.NotNull(store.Get(imageBack.Id)!.ThumbnailPng);
        Assert.True(File.Exists(imageBack.OriginalPath));

        var group = Assert.Single(store.Groups());
        Assert.Equal("甲", group.Icon);
    }

    [Fact]
    public void An_encrypted_round_trip_works_and_a_wrong_password_refuses()
    {
        using var store = SeededStore();
        var backup = Path.Combine(_images, "sealed.shiyubk");

        BackupArchive.Export(backup, store, _images, settingsJson: null, PasswordOf("correct horse"));

        var magic = File.ReadAllBytes(backup);
        Assert.Equal("SHIYUBK1"u8.ToArray(), magic.Take(8).ToArray());

        var wrong = Assert.Throws<BackupException>(() =>
            BackupArchive.Import(backup, store, _images, overwrite: false, PasswordOf("battery staple")));
        Assert.Contains("口令", wrong.Message);

        store.ClearAll();
        var outcome = BackupArchive.Import(
            backup, store, _images, overwrite: true, PasswordOf("correct horse"));
        Assert.Equal(3, outcome.Added);
    }

    [Fact]
    public void An_encrypted_backup_refuses_to_open_without_a_password()
    {
        using var store = SeededStore();
        var backup = Path.Combine(_images, "locked.shiyubk");
        BackupArchive.Export(backup, store, _images, settingsJson: null, PasswordOf("secret"));

        var error = Assert.Throws<BackupException>(() =>
            BackupArchive.Import(backup, store, _images, overwrite: false, passwordUtf16: null));
        Assert.Contains("口令", error.Message);
    }

    [Fact]
    public void Merging_twice_adds_once_and_skips_the_rest()
    {
        using var store = SeededStore();
        var backup = Path.Combine(_images, "merge.shiyubk");
        BackupArchive.Export(backup, store, _images, settingsJson: null, passwordUtf16: null);

        var first = BackupArchive.Import(
            backup, store, _images, overwrite: false, passwordUtf16: null);
        Assert.Equal(3, first.SkippedExisting);
        Assert.Equal(0, first.Added);
        Assert.Null(first.SettingsJson);

        using var other = EntryStore.Open(new TempDatabase().FilePath);
        other.Append("something new", "ZCode", Noon);
        BackupArchive.Import(
            backup, other, _images, overwrite: false, passwordUtf16: null);

        Assert.Equal(4, other.Count());
        Assert.Single(other.Recent(limit: 10), entry => entry.Text == "something new");
    }

    [Fact]
    public void A_merge_decides_duplicates_by_content_not_timestamp()
    {
        using var store = EntryStore.Open(_database.FilePath);
        store.Append("same words", "ZCode", Noon);

        using var other = EntryStore.Open(new TempDatabase().FilePath);
        other.Append("same words", "another machine", Noon.AddYears(1));

        var backup = Path.Combine(_images, "time.shiyubk");
        BackupArchive.Export(backup, other, _images, settingsJson: null, passwordUtf16: null);

        var outcome = BackupArchive.Import(
            backup, store, _images, overwrite: false, passwordUtf16: null);
        Assert.Equal(1, outcome.SkippedExisting);
        Assert.Equal(0, outcome.Added);
    }

    [Fact]
    public void A_foreign_or_broken_file_is_refused_with_plain_words()
    {
        using var store = EntryStore.Open(_database.FilePath);

        var junk = Path.Combine(_images, "junk.shiyubk");
        File.WriteAllBytes(junk, "definitely not a backup"u8.ToArray());
        var error = Assert.Throws<BackupException>(() =>
            BackupArchive.Import(junk, store, _images, overwrite: false, passwordUtf16: null));
        Assert.Contains("拾语", error.Message);

        var truncated = Path.Combine(_images, "cut.shiyubk");
        File.WriteAllBytes(truncated, "SHIYUBK1"u8.ToArray());
        Assert.Throws<BackupException>(() =>
            BackupArchive.Import(truncated, store, _images, overwrite: false, PasswordOf("x")));

        // A refused import leaves what was there.
        store.Append("survivor", "ZCode", Noon);
        Assert.Throws<BackupException>(() =>
            BackupArchive.Import(junk, store, _images, overwrite: false, passwordUtf16: null));
        Assert.Equal(1, store.Count());
    }

    [Fact]
    public void Overwrite_import_replaces_history_entirely()
    {
        using var store = SeededStore();
        store.Append("extra that should vanish", "ZCode", Noon.AddHours(1));

        var backup = Path.Combine(_images, "replace.shiyubk");
        BackupArchive.Export(backup, store, _images, settingsJson: null, passwordUtf16: null);

        using var target = EntryStore.Open(new TempDatabase().FilePath);
        target.Append("old world", "explorer", Noon);
        var outcome = BackupArchive.Import(
            backup, target, _images, overwrite: true, passwordUtf16: null);

        Assert.Equal(4, outcome.Added);
        Assert.Equal(4, target.Count());
        Assert.DoesNotContain(target.Recent(limit: 20), entry => entry.Text == "old world");
    }

    [Fact]
    public void The_password_buffer_is_wiped_after_use()
    {
        using var store = SeededStore();
        var backup = Path.Combine(_images, "wipe.shiyubk");
        var password = PasswordOf("wipe me");

        BackupArchive.Export(backup, store, _images, settingsJson: null, password);

        Assert.All(password, bitten => Assert.Equal(0, bitten));
    }

    [Fact]
    public void The_fingerprint_ignores_when_and_counts_content()
    {
        var one = new Entry(1, "text", "a", Noon);
        var two = new Entry(2, "text", "b", Noon.AddYears(3)) { Favorite = true, Note = "renamed" };
        var three = new Entry(3, "different", "a", Noon);

        Assert.Equal(ContentFingerprint.Of(one), ContentFingerprint.Of(two));
        Assert.NotEqual(ContentFingerprint.Of(one), ContentFingerprint.Of(three));
    }

    // --- atomicity (O-03) ---------------------------------------------------

    /// <summary>
    /// A moment-in-time picture of the library and its images — one string, so
    /// two pictures compare by content and a diff names what moved.
    /// </summary>
    private static string Snapshot(EntryStore store, string imagesDirectory)
        => string.Join("|",
            store.Count(),
            string.Join(",", store.Recent(limit: 100).Select(entry => entry.Text).OrderBy(text => text)),
            string.Join(",", store.Groups().Select(group => group.Name + (group.Hidden ? "!" : "")).OrderBy(name => name)),
            string.Join(",", store.AllTags().OrderBy(tag => tag)),
            Directory.Exists(imagesDirectory)
                ? string.Join(",", Directory.GetFiles(imagesDirectory).Select(Path.GetFileName).OrderBy(name => name))
                : string.Empty);

    [Fact]
    public void An_overwrite_that_fails_part_way_leaves_the_library_and_the_images_exactly_as_they_were()
    {
        using var source = SeededStore();
        var backup = Path.Combine(_images, "half.shiyubk");
        BackupArchive.Export(backup, source, _images, settingsJson: null, passwordUtf16: null);

        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        store.Append("old world", "explorer", Noon);
        store.CreateGroup("旧分组");
        File.WriteAllBytes(Path.Combine(_images, "existing.png"), [9, 9, 9, 9]);

        // Fails the import after the clear, on the very first insert — the
        // point where the old implementation had already committed an empty
        // history. RAISE(ABORT) is what a constraint or disk failure looks
        // like to SqliteTransaction; a TEMP trigger leaves no schema trace
        // once the connection is gone.
        using (var sabotage = store.Connection.CreateCommand())
        {
            sabotage.CommandText = """
                CREATE TEMP TRIGGER break_import AFTER INSERT ON entries
                BEGIN SELECT RAISE(ABORT, 'import cut short'); END;
                """;
            sabotage.ExecuteNonQuery();
        }

        var before = Snapshot(store, _images);
        var failure = Assert.Throws<BackupException>(() =>
            BackupArchive.Import(backup, store, _images, overwrite: true, passwordUtf16: null));

        Assert.Contains("撤回", failure.Message);
        Assert.Equal(before, Snapshot(store, _images));
        Assert.False(Directory.Exists(_images + ".import"), "the staging directory must not outlive a failed import");

        // And the library is still usable: removing the fault and retrying
        // lands the whole import, images included.
        using (var repair = store.Connection.CreateCommand())
        {
            repair.CommandText = "DROP TRIGGER break_import;";
            repair.ExecuteNonQuery();
        }

        var outcome = BackupArchive.Import(backup, store, _images, overwrite: true, passwordUtf16: null);
        Assert.Equal(3, outcome.Added);
        // The original was already on disk from the fixture, so nothing moves;
        // the entry still points at a file that is there.
        Assert.True(File.Exists(Path.Combine(_images, "original.png")));
    }

    [Fact]
    public void A_backup_with_unreadable_entries_is_refused_before_a_single_row_changes()
    {
        var backup = Path.Combine(_images, "corrupt.shiyubk");
        using (var zip = ZipFile.Open(backup, ZipArchiveMode.Create))
        {
            ZipEntry(zip, "manifest.json", """{"Format":1,"CreatedAtMs":0,"Entries":2,"Images":0,"Encrypted":false}""");
            ZipEntry(zip, "groups.json", "[]");
            ZipEntry(zip, "settings.json", "{}");
            ZipEntry(zip, "entries.json", "[{\"Text\":\"broken");
        }

        using var store = EntryStore.Open(_database.FilePath);
        store.Append("survivor", "ZCode", Noon);
        var before = Snapshot(store, _images);

        var failure = Assert.Throws<BackupException>(() =>
            BackupArchive.Import(backup, store, _images, overwrite: true, passwordUtf16: null));
        Assert.Contains("损坏", failure.Message);

        Assert.Equal(before, Snapshot(store, _images));
        Assert.False(Directory.Exists(_images + ".import"));
    }

    [Fact]
    public void An_overwrite_into_a_fresh_images_directory_lands_counts_groups_and_originals()
    {
        using var source = SeededStore();
        var backup = Path.Combine(_images, "fresh.shiyubk");
        BackupArchive.Export(backup, source, _images, settingsJson: null, passwordUtf16: null);

        var freshImages = Path.Combine(_images, "fresh-images");
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        var outcome = BackupArchive.Import(
            backup, store, freshImages, overwrite: true, passwordUtf16: null);

        Assert.Equal(3, outcome.Added);
        Assert.Equal(1, outcome.ImagesRestored);
        Assert.Equal(1, outcome.GroupsCreated);
        Assert.Equal(3, store.Count());

        var image = store.Recent(limit: 10).Single(entry => entry.Kind == EntryKind.Image);
        Assert.Equal(Path.Combine(freshImages, "original.png"), image.OriginalPath);
        Assert.True(File.Exists(image.OriginalPath));
        Assert.Equal("甲", Assert.Single(store.Groups()).Icon);
        Assert.False(Directory.Exists(freshImages + ".import"));
    }

    [Fact]
    public void A_merge_that_fails_part_way_also_rolls_back_to_the_letter()
    {
        using var other = EntryStore.Open(new TempDatabase().FilePath);
        other.Append("same words", "ZCode", Noon);
        other.Append("brand new", "ZCode", Noon.AddMinutes(1));
        var backup = Path.Combine(_images, "merge-fail.shiyubk");
        BackupArchive.Export(backup, other, _images, settingsJson: null, passwordUtf16: null);

        using var store = EntryStore.Open(_database.FilePath);
        store.Append("same words", "another machine", Noon);

        using (var sabotage = store.Connection.CreateCommand())
        {
            sabotage.CommandText = """
                CREATE TEMP TRIGGER break_merge AFTER INSERT ON entries
                BEGIN SELECT RAISE(ABORT, 'merge cut short'); END;
                """;
            sabotage.ExecuteNonQuery();
        }

        var before = Snapshot(store, _images);
        Assert.Throws<BackupException>(() =>
            BackupArchive.Import(backup, store, _images, overwrite: false, passwordUtf16: null));

        Assert.Equal(before, Snapshot(store, _images));
        Assert.False(Directory.Exists(_images + ".import"));
    }

    private static void ZipEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
