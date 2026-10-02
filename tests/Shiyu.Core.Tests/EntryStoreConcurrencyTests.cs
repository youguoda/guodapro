using System.Collections.Concurrent;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// The store is one SQLite connection with callers on more than one thread —
/// image recording, the retention sweep and backups all run off the UI
/// thread. Every public call has to be safe from anywhere, and an id handed
/// back has to name the row that call wrote, not one another thread wrote a
/// moment later.
/// </summary>
public class EntryStoreConcurrencyTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Four_threads_sharing_one_store_never_collide_and_every_id_names_its_own_row()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);

        const int Threads = 4;
        const int StepsEach = 250;

        var appended = new ConcurrentBag<(long Id, string Text)>();
        var imported = new ConcurrentBag<(string Text, string Group)>();
        var created = new ConcurrentBag<(long Id, string Name)>();
        var failures = new ConcurrentQueue<Exception>();
        using var start = new Barrier(Threads);

        var workers = Enumerable.Range(0, Threads).Select(worker => new Thread(() =>
        {
            start.SignalAndWait();
            try
            {
                for (var step = 0; step < StepsEach; step++)
                {
                    var marker = $"t{worker}-{step}";
                    switch (step % 5)
                    {
                        case 0:
                            appended.Add((store.Append(marker, "stress", Noon.AddSeconds(step)).Id, marker));
                            break;
                        case 1:
                            store.ImportEntry(
                                new Entry(0, marker, "stress", Noon.AddSeconds(step)) { Tags = [marker] },
                                groupName: $"g{worker}");
                            imported.Add((marker, $"g{worker}"));
                            break;
                        case 2:
                            created.Add((store.CreateGroup(marker), marker));
                            break;
                        case 3:
                            store.Recent(limit: 20);
                            break;
                        default:
                            store.Count();
                            break;
                    }
                }
            }
            catch (Exception failure)
            {
                failures.Enqueue(failure);
            }
        })).ToList();

        workers.ForEach(thread => thread.Start());
        workers.ForEach(thread => thread.Join());

        Assert.Empty(failures);

        foreach (var (id, text) in appended)
        {
            Assert.Equal(text, store.Get(id)?.Text);
        }

        var groupNames = store.Groups().ToDictionary(group => group.Id, group => group.Name);
        foreach (var (id, name) in created)
        {
            Assert.Equal(name, groupNames[id]);
        }

        // An import hands back no id, but files the row's tags and group
        // under the one it read: a tag on someone else's row is a wrong id.
        var everything = store.Page(limit: 10_000, offset: 0).ToDictionary(entry => entry.Text);
        foreach (var (text, group) in imported)
        {
            var entry = everything[text];
            Assert.Equal([text], entry.Tags);
            Assert.Equal(group, groupNames[entry.GroupId!.Value]);
        }

        Assert.All(appended, row => Assert.Empty(everything[row.Text].Tags));
        Assert.Equal(appended.Count + imported.Count, store.Count());
        Assert.Equal(created.Count + Threads, groupNames.Count);
    }

    [Fact]
    public void A_write_batch_commits_as_one_or_not_at_all()
    {
        using var database = new TempDatabase();
        using var store = EntryStore.Open(database.FilePath);
        store.Append("kept before", "ZCode", Noon);
        store.CreateGroup("原分组");

        Assert.Throws<InvalidOperationException>(() => store.RunInTransaction(() =>
        {
            store.ClearAll();
            store.CreateGroup("不会留下的分组");
            store.ImportEntry(new Entry(0, "不会留下的条目", "ZCode", Noon.AddMinutes(1)), "不会留下的分组");
            throw new InvalidOperationException("boom");
        }));

        // A half-imported library is exactly what the batch exists to prevent:
        // nothing from the failed run may be there, and everything before it
        // must still be.
        Assert.Equal(1, store.Count());
        Assert.Equal("kept before", store.Recent(limit: 10).Single().Text);
        Assert.Equal("原分组", Assert.Single(store.Groups()).Name);
        Assert.Empty(store.AllTags());

        store.RunInTransaction(() =>
        {
            store.ClearAll();
            var group = store.CreateGroup("恢复的分组");
            store.SetGroupHidden(group, true);
            store.ImportEntry(
                new Entry(0, "恢复的条目", "ZCode", Noon.AddMinutes(2)) { IsPinned = true, Tags = ["回来了"] },
                "恢复的分组");
        });

        var restored = store.Recent(limit: 10).Single();
        Assert.Equal("恢复的条目", restored.Text);
        Assert.True(restored.IsPinned);
        Assert.Equal(["回来了"], restored.Tags);
        var group = Assert.Single(store.Groups());
        Assert.Equal("恢复的分组", group.Name);
        Assert.True(group.Hidden);
        Assert.Equal(group.Id, restored.GroupId);

        // Nested batches join the outer one — SQLite cannot nest transactions,
        // and a batch inside a batch is a caller keeping its own atomicity.
        store.RunInTransaction(() => store.RunInTransaction(() => store.Append("nested", "ZCode", Noon)));
        Assert.Equal(2, store.Count());
    }
}
