using Microsoft.Data.Sqlite;

namespace Shiyu.Core;

/// <summary>
/// A group: vertical containment for entries. Tags answer "show me everything
/// marked 待办"; a group answers "show me project 甲's pile". One is a label
/// that stacks, the other is a drawer that switches — so they stay separate.
/// </summary>
public sealed record EntryGroup(long Id, string Name, string? Icon, int Position, bool Hidden);

public sealed partial class EntryStore
{
    /// <summary>
    /// Every group in the user's order. Hidden ones included: the switcher
    /// row hides them, but the manage list and the assign picker show all.
    /// </summary>
    public IReadOnlyList<EntryGroup> Groups()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, icon, position, hidden
            FROM groups
            ORDER BY position, id;
            """;

        var groups = new List<EntryGroup>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            groups.Add(new EntryGroup(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4) != 0));
        }

        return groups;
    }

    /// <summary>Creates a group, appended at the end of the order. Returns its id.</summary>
    public long CreateGroup(string name, string? icon = null)
    {
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO groups (name, icon, position)
                VALUES ($name, $icon, (SELECT COALESCE(MAX(position), 0) + 1 FROM groups));
                """;
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$icon", (object?)icon ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        using var read = _connection.CreateCommand();
        read.CommandText = "SELECT last_insert_rowid();";
        return (long)read.ExecuteScalar()!;
    }

    public void RenameGroup(long id, string name)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE groups SET name = $name WHERE id = $id;";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetGroupIcon(long id, string? icon)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE groups SET icon = $icon WHERE id = $id;";
        command.Parameters.AddWithValue("$icon", (object?)icon ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A hidden group keeps its entries and stays assignable, but leaves the
    /// switcher row — for the pile the user is not switching to right now.
    /// </summary>
    public void SetGroupHidden(long id, bool hidden)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE groups SET hidden = $hidden WHERE id = $id;";
        command.Parameters.AddWithValue("$hidden", hidden ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Swaps a group with its neighbour in the ordering. Moving past either
    /// end is a no-op: the row is a short list, not a free-form canvas.
    /// </summary>
    public void MoveGroup(long id, int delta)
    {
        var groups = Groups();
        var index = 0;
        while (index < groups.Count && groups[index].Id != id)
        {
            index++;
        }

        var swapWith = index + delta;

        if (index >= groups.Count || swapWith < 0 || swapWith >= groups.Count)
        {
            return;
        }

        using var transaction = _connection.BeginTransaction();
        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE groups SET position = $position WHERE id = $id;";

            var position = command.Parameters.Add("$position", SqliteType.Integer);
            var target = command.Parameters.Add("$id", SqliteType.Integer);

            position.Value = groups[swapWith].Position;
            target.Value = groups[index].Id;
            command.ExecuteNonQuery();

            position.Value = groups[index].Position;
            target.Value = groups[swapWith].Id;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Deletes a group. Its entries are not deleted — the foreign key clears
    /// their column and they return to ungrouped, exactly as promised.
    /// </summary>
    public void DeleteGroup(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM groups WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Files an entry into a group, or removes it with a null group.</summary>
    public void SetEntryGroup(long entryId, long? groupId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE entries SET group_id = $group WHERE id = $id;";
        command.Parameters.AddWithValue("$group", (object?)groupId ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", entryId);
        command.ExecuteNonQuery();
    }
}
