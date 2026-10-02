using Microsoft.Data.Sqlite;

namespace Shiyu.Core;

public sealed partial class EntryStore
{
    /// <summary>
    /// Wipes history and groups for an overwrite-import. Tags go with their
    /// entries (cascade); the application-icon cache stays — it is a cache,
    /// not history, and a restore should not blank icons it still shares.
    /// </summary>
    public void ClearAll()
    {
        lock (_gate)
        {
            using var write = BeginWrite();
            ExecuteIn(write.Transaction, "DELETE FROM entries;");
            ExecuteIn(write.Transaction, "DELETE FROM groups;");
            write.Commit();
            CountChanged();
        }
    }

    /// <summary>
    /// Writes one entry with every field intact — the import path. The
    /// entry's group travels by name (names are what a user recognises in a
    /// file they may inspect) and is created here when missing, so a merge
    /// can never fail because the other machine had one group more.
    /// </summary>
    public void ImportEntry(Entry entry, string? groupName)
    {
        lock (_gate)
        {
            using var write = BeginWrite();
            var transaction = write.Transaction;

            long? groupId = null;
            if (groupName is { Length: > 0 } name)
            {
                groupId = ResolveGroupIdIn(transaction, name);
            }

            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO entries (
                        text, source_app, created_at, kind, thumbnail, original_path,
                        pinned, sub_type, html, rtf, files, favorite, note, use_count, group_id,
                        image_width, image_height)
                    VALUES (
                        $text, $sourceApp, $createdAt, $kind, $thumbnail, $originalPath,
                        $pinned, $subType, $html, $rtf, $files, $favorite, $note, $useCount, $groupId,
                        $w, $h)
                    RETURNING id;
                    """;

                command.Parameters.AddWithValue("$text", entry.Text);
                command.Parameters.AddWithValue("$sourceApp", (object?)entry.SourceApp ?? DBNull.Value);
                command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue("$kind", (int)entry.Kind);
                command.Parameters.AddWithValue("$thumbnail", (object?)entry.ThumbnailPng ?? DBNull.Value);
                command.Parameters.AddWithValue("$originalPath", (object?)entry.OriginalPath ?? DBNull.Value);
                command.Parameters.AddWithValue("$pinned", entry.IsPinned ? 1 : 0);
                command.Parameters.AddWithValue("$subType", (object?)SubtypeOf(entry) ?? DBNull.Value);
                command.Parameters.AddWithValue("$html", (object?)entry.Html ?? DBNull.Value);
                command.Parameters.AddWithValue("$rtf", (object?)entry.Rtf ?? DBNull.Value);
                command.Parameters.AddWithValue("$files", (object?)(entry.Files.Count > 0 ? string.Join("\n", entry.Files) : null) ?? DBNull.Value);
                command.Parameters.AddWithValue("$favorite", entry.Favorite ? 1 : 0);
                command.Parameters.AddWithValue("$note", (object?)entry.Note ?? DBNull.Value);
                command.Parameters.AddWithValue("$useCount", entry.UseCount);
                command.Parameters.AddWithValue("$groupId", (object?)groupId ?? DBNull.Value);

                // A backup made before the size columns existed imports zeros;
                // the thumbnail is right there in the row and knows the shape.
                var (width, height) = (entry.ImageWidth, entry.ImageHeight);
                if (width == 0 && entry.ThumbnailPng is { Length: > 0 } png && PngSize.Read(png) is { } size)
                {
                    (width, height) = (size.Width, size.Height);
                }

                command.Parameters.AddWithValue("$w", width);
                command.Parameters.AddWithValue("$h", height);

                var id = (long)command.ExecuteScalar()!;
                CountChanged();

                foreach (var tag in entry.Tags)
                {
                    using var attach = _connection.CreateCommand();
                    attach.Transaction = transaction;
                    attach.CommandText = "INSERT OR IGNORE INTO tags (name) VALUES ($name);";
                    attach.Parameters.AddWithValue("$name", tag);
                    attach.ExecuteNonQuery();

                    using var link = _connection.CreateCommand();
                    link.Transaction = transaction;
                    link.CommandText = """
                        INSERT OR IGNORE INTO entry_tags (entry_id, tag_id)
                        VALUES ($entry, (SELECT id FROM tags WHERE name = $name));
                        """;
                    link.Parameters.AddWithValue("$entry", id);
                    link.Parameters.AddWithValue("$name", tag);
                    link.ExecuteNonQuery();
                }
            }

            write.Commit();
        }
    }

    /// <summary>
    /// Always a value, 'None' included: imports write classified rows, never
    /// NULLs a future migration would have to sweep (O-22).
    /// </summary>
    private static string SubtypeOf(Entry entry) => entry.Subtype.ToString();

    private long ResolveGroupIdIn(SqliteTransaction transaction, string name)
    {
        using (var find = _connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT id FROM groups WHERE name = $name;";
            find.Parameters.AddWithValue("$name", name);
            if (find.ExecuteScalar() is { } found)
            {
                return (long)found;
            }
        }

        using var create = _connection.CreateCommand();
        create.Transaction = transaction;
        create.CommandText = """
            INSERT INTO groups (name, icon, position)
            VALUES ($name, NULL, (SELECT COALESCE(MAX(position), 0) + 1 FROM groups))
            RETURNING id;
            """;
        create.Parameters.AddWithValue("$name", name);
        return (long)create.ExecuteScalar()!;
    }

    private void ExecuteIn(SqliteTransaction transaction, string sql)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
