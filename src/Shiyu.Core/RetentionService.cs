namespace Shiyu.Core;

/// <param name="Removed">Originals deleted from disk this sweep.</param>
/// <param name="Reclaimed">Entries whose original was already missing.</param>
public sealed record RetentionResult(int Removed, int Reclaimed)
{
    public int Total => Removed + Reclaimed;
}

/// <summary>
/// Deletes image originals once they are old enough, so disk use stays bounded.
///
/// Only image originals. Text entries are never cleaned up at any age: they are
/// tiny, and they are the thing the user actually goes looking for months
/// later. Thumbnails are never cleaned up either — they are what keeps the
/// history looking complete after the originals are gone.
/// </summary>
public sealed class RetentionService(
    EntryStore store,
    ImageArchive archive,
    TimeProvider clock)
{
    /// <summary>
    /// Worked through in batches so a long-unused installation with a large
    /// backlog cannot hold the database open for an unbounded stretch.
    /// </summary>
    private const int BatchSize = 200;

    /// <summary>
    /// Removes originals older than <paramref name="retention"/>.
    ///
    /// Safe to interrupt at any point. The file is deleted before the entry
    /// forgets where it was, deliberately: crashing between the two leaves an
    /// entry pointing at a file that is gone, which every read path already
    /// copes with, and which the next sweep tidies. The other order would leave
    /// a file nothing references and nothing will ever clean up.
    /// </summary>
    public RetentionResult Sweep(TimeSpan retention)
    {
        var cutoff = clock.GetUtcNow() - retention;
        var removed = 0;
        var reclaimed = 0;

        while (true)
        {
            var batch = store.ImagesWithOriginalsBefore(cutoff, BatchSize);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var entry in batch)
            {
                if (entry.OriginalPath is not { Length: > 0 } path)
                {
                    continue;
                }

                if (!File.Exists(path))
                {
                    // Already gone: an interrupted sweep, or a user who emptied
                    // the folder by hand. Either way the entry should stop
                    // claiming to have one.
                    store.ClearOriginal(entry.Id);
                    reclaimed++;
                    continue;
                }

                if (archive.Delete(path))
                {
                    store.ClearOriginal(entry.Id);
                    removed++;
                }
                else
                {
                    // Locked by something else right now. Left alone so the
                    // next sweep can try again rather than losing track of it.
                    reclaimed += 0;
                }
            }

            // A batch that cleared nothing means everything left in it is
            // stuck; stopping avoids spinning on the same rows forever.
            if (batch.Count < BatchSize || removed + reclaimed == 0)
            {
                break;
            }
        }

        return new RetentionResult(removed, reclaimed);
    }
}
