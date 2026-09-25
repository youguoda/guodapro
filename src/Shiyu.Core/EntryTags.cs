using Microsoft.Data.Sqlite;

namespace Shiyu.Core;

/// <summary>Tagging and pinning, kept apart from the store's core reads and writes.</summary>
public sealed partial class EntryStore
{
    /// <summary>
    /// Pins or unpins an entry. Pinned entries sort ahead of everything else,
    /// because the ones worth pinning are the ones reached for constantly and
    /// chronology stops helping with those.
    /// </summary>
    public void SetPinned(long id, bool pinned)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE entries SET pinned = $pinned WHERE id = $id;";
        command.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Favourites or unfavourites an entry. A favourite never moves the entry:
    /// belonging to a collection is the pin's job, a favourite only joins one.
    /// </summary>
    public void SetFavorite(long id, bool favorite)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE entries SET favorite = $favorite WHERE id = $id;";
        command.Parameters.AddWithValue("$favorite", favorite ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Sets, rewrites, or (with null) removes an entry's note.</summary>
    public void SetNote(long id, string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE entries SET note = $note WHERE id = $id;";
        command.Parameters.AddWithValue("$note", (object?)trimmed ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Counts one more use — the entry was copied or pasted back into the
    /// world again.
    /// </summary>
    public void BumpUse(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE entries SET use_count = use_count + 1 WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Attaches a tag, creating it if this is its first use. Comparison ignores
    /// case, so "Work" and "work" are one tag rather than two that look alike.
    /// </summary>
    public void AddTag(long entryId, string tag)
    {
        var name = tag.Trim();
        if (name.Length == 0)
        {
            return;
        }

        using var transaction = _connection.BeginTransaction();

        using (var insertTag = _connection.CreateCommand())
        {
            insertTag.CommandText = "INSERT OR IGNORE INTO tags (name) VALUES ($name);";
            insertTag.Parameters.AddWithValue("$name", name);
            insertTag.ExecuteNonQuery();
        }

        using (var link = _connection.CreateCommand())
        {
            link.CommandText = """
                INSERT OR IGNORE INTO entry_tags (entry_id, tag_id)
                SELECT $entryId, id FROM tags WHERE name = $name;
                """;
            link.Parameters.AddWithValue("$entryId", entryId);
            link.Parameters.AddWithValue("$name", name);
            link.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Detaches a tag from one entry. The tag itself survives — it is almost
    /// certainly on other entries, and removing it here should not disturb them.
    /// </summary>
    public void RemoveTag(long entryId, string tag)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            DELETE FROM entry_tags
            WHERE entry_id = $entryId
              AND tag_id IN (SELECT id FROM tags WHERE name = $name);
            """;
        command.Parameters.AddWithValue("$entryId", entryId);
        command.Parameters.AddWithValue("$name", tag.Trim());
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> TagsOf(long entryId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT t.name
            FROM tags t
            JOIN entry_tags et ON et.tag_id = t.id
            WHERE et.entry_id = $entryId
            ORDER BY t.name;
            """;
        command.Parameters.AddWithValue("$entryId", entryId);

        return ReadNames(command);
    }

    /// <summary>Every tag in use, for offering as choices.</summary>
    public IReadOnlyList<string> AllTags()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT t.name
            FROM tags t
            JOIN entry_tags et ON et.tag_id = t.id
            ORDER BY t.name;
            """;

        return ReadNames(command);
    }

    /// <summary>
    /// Deletes a tag everywhere it is used. The entries themselves are
    /// untouched — losing content because a label was tidied away would be an
    /// unpleasant surprise.
    /// </summary>
    public void DeleteTag(string tag)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM tags WHERE name = $name;";
        command.Parameters.AddWithValue("$name", tag.Trim());
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> ReadNames(SqliteCommand command)
    {
        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
