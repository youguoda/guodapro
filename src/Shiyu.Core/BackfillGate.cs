namespace Shiyu.Core;

/// <summary>
/// The generation counter that decides whether a late result from a
/// background thread may still land in the user interface.
///
/// O-36's rendering contract: the interface renders first with a placeholder,
/// the decode or probe runs on the thread pool, and the result comes back
/// later — by which time the list may have been rebuilt, the selection moved
/// on, or the window hidden. A backfill that no longer describes the world on
/// screen must be dropped, or it writes a stranger's thumbnail into a
/// recycled row.
///
/// The counter only moves when the owner says the world changed
/// (<see cref="Invalidate"/> — a rebuild, a selection switch). Parallel
/// requests within one generation all pass <see cref="IsCurrent"/>, because
/// they describe different cards of the same, still-valid world.
/// </summary>
public sealed class BackfillGate
{
    private long _epoch;

    /// <summary>The current generation. Capture before starting background work.</summary>
    public long Epoch => System.Threading.Volatile.Read(ref _epoch);

    /// <summary>Declares everything captured before now out of date.</summary>
    public void Invalidate() => System.Threading.Interlocked.Increment(ref _epoch);

    /// <summary>Whether a captured generation is still the current one.</summary>
    public bool IsCurrent(long seen) => seen == System.Threading.Volatile.Read(ref _epoch);
}
