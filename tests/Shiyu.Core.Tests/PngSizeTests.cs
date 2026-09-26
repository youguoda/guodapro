using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class PngSizeTests
{
    [Fact]
    public void It_reads_the_dimensions_out_of_a_png_header()
    {
        var png = Png(1920, 1080);

        Assert.Equal((1920, 1080), PngSize.Read(png));
    }

    [Fact]
    public void It_reads_small_dimensions_too()
    {
        Assert.Equal((1, 1), PngSize.Read(Png(1, 1)));
        Assert.Equal((320, 200), PngSize.Read(Png(320, 200)));
    }

    [Fact]
    public void Bytes_that_are_not_a_png_are_refused_rather_than_guessed()
    {
        Assert.Null(PngSize.Read("this is a text file"u8.ToArray()));
        Assert.Null(PngSize.Read([]));
        Assert.Null(PngSize.Read(new byte[23])); // Signature-length but short.
    }

    [Fact]
    public void Zero_dimensions_are_refused_so_callers_never_divide_by_them()
    {
        Assert.Null(PngSize.Read(Png(0, 100)));
        Assert.Null(PngSize.Read(Png(100, 0)));
    }

    /// <summary>A PNG-shaped header: signature, IHDR, big-endian size. Enough for the reader.</summary>
    private static byte[] Png(int width, int height)
    {
        var png = new byte[24];
        png[0] = 0x89;
        png[1] = 0x50;
        png[2] = 0x4E;
        png[3] = 0x47;
        png[4] = 0x0D;
        png[5] = 0x0A;
        png[6] = 0x1A;
        png[7] = 0x0A;
        png[12] = (byte)'I';
        png[13] = (byte)'H';
        png[14] = (byte)'D';
        png[15] = (byte)'R';
        png[16] = (byte)(width >> 24);
        png[17] = (byte)(width >> 16);
        png[18] = (byte)(width >> 8);
        png[19] = (byte)width;
        png[20] = (byte)(height >> 24);
        png[21] = (byte)(height >> 16);
        png[22] = (byte)(height >> 8);
        png[23] = (byte)height;
        return png;
    }
}

public class ImageSizeMigrationTests
{
    [Fact]
    public void An_image_recorded_with_its_size_reads_back_with_it()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        var entry = store.AppendImage(
            "图片 800×600", new byte[] { 1, 2, 3 }, @"C:\ somewhere.png",
            "test", new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero),
            width: 800, height: 600);

        var read = Assert.Single(store.Recent(limit: 5));

        Assert.Equal(800, read.ImageWidth);
        Assert.Equal(600, read.ImageHeight);
        Assert.Equal(entry.Id, read.Id);
    }

    [Fact]
    public void A_database_from_before_the_size_columns_gets_them_from_the_thumbnail()
    {
        using var database = new TempDatabase();

        // The v10 shape: everything through translated_from, no size columns.
        using (var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database.FilePath}"))
        {
            old.Open();
            using var create = old.CreateCommand();
            create.CommandText = """
                CREATE TABLE entries (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    text        TEXT    NOT NULL,
                    source_app  TEXT    NULL,
                    created_at  INTEGER NOT NULL,
                    kind        INTEGER NOT NULL DEFAULT 0,
                    thumbnail   BLOB    NULL,
                    original_path TEXT  NULL,
                    pinned      INTEGER NOT NULL DEFAULT 0,
                    sub_type    TEXT    NULL,
                    html        TEXT    NULL,
                    rtf         TEXT    NULL,
                    files       TEXT    NULL,
                    favorite    INTEGER NOT NULL DEFAULT 0,
                    note        TEXT    NULL,
                    use_count   INTEGER NOT NULL DEFAULT 0,
                    group_id    INTEGER NULL,
                    translated_from INTEGER NULL
                );
                CREATE TABLE tags (
                    id   INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL COLLATE NOCASE UNIQUE
                );
                CREATE TABLE entry_tags (
                    entry_id INTEGER NOT NULL REFERENCES entries(id) ON DELETE CASCADE,
                    tag_id   INTEGER NOT NULL REFERENCES tags(id)    ON DELETE CASCADE,
                    PRIMARY KEY (entry_id, tag_id)
                );
                CREATE INDEX idx_entry_tags_tag ON entry_tags (tag_id);
                PRAGMA user_version = 10;
                """;
            create.ExecuteNonQuery();

            using var insert = old.CreateCommand();
            insert.CommandText = """
                INSERT INTO entries (text, kind, thumbnail, created_at)
                VALUES ('图片 640×480', 1, $png, 1758542400000);
                """;
            var png = new byte[24];
            png[0] = 0x89; png[1] = 0x50; png[2] = 0x4E; png[3] = 0x47;
            png[4] = 0x0D; png[5] = 0x0A; png[6] = 0x1A; png[7] = 0x0A;
            png[12] = (byte)'I'; png[13] = (byte)'H'; png[14] = (byte)'D'; png[15] = (byte)'R';
            png[18] = 640 >> 8;
            png[19] = 640 & 0xFF;
            png[22] = 480 >> 8;
            png[23] = 480 & 0xFF;
            insert.Parameters.AddWithValue("$png", png);
            insert.ExecuteNonQuery();
        }

        using var store = EntryStore.Open(database.FilePath);
        var image = Assert.Single(store.Recent(limit: 5));

        Assert.Equal(640, image.ImageWidth);
        Assert.Equal(480, image.ImageHeight);
    }

    [Fact]
    public void Text_rows_are_never_touched_by_the_backfill()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        store.Append("plain text", "test", new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero));

        var entry = Assert.Single(store.Recent(limit: 5));

        Assert.Equal(0, entry.ImageWidth);
        Assert.Equal(0, entry.ImageHeight);
    }
}
