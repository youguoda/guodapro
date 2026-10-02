namespace Shiyu.Core;

/// <summary>What a probe found out about one path.</summary>
/// <param name="Exists">Either kind of file — a file or a directory — is there.</param>
/// <param name="IsDirectory">Whether the path names a directory rather than a file.</param>
public readonly record struct FileVerdict(bool Exists, bool IsDirectory);

/// <summary>
/// What the renderer asks before it ever touches the disk: the last verdict
/// a background probe recorded, while that verdict is still fresh.
///
/// O-36's reason for being: a path that lives on an offline network drive
/// makes <c>File.Exists</c> wait out an SMB timeout, and the narrow bar once
/// paid that price on the UI thread for every card, every summon. The cache
/// answers from memory; the first probe for a path happens on the thread
/// pool and lands in the cache for the next render to read.
///
/// Verdicts expire rather than being revoked: files come and go between
/// summons, so a verdict is only trusted for a short while before a probe is
/// worth scheduling again. The clock is injected so tests can age a verdict
/// without sleeping.
/// </summary>
public sealed class FileExistenceCache
{
    /// <summary>How long a verdict is trusted before a probe is worth another look.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(15);

    private readonly Func<long> _clock;
    private readonly long _ttlMilliseconds;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Slot> _slots =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct Slot(bool Exists, bool IsDirectory, long RecordedAt);

    private static long DefaultClock() => Environment.TickCount64;

    /// <param name="ttl">How long a verdict stays fresh. Null for <see cref="DefaultTtl"/>.</param>
    /// <param name="clock">Monotonic milliseconds; null for the system tick count.</param>
    public FileExistenceCache(TimeSpan? ttl = null, Func<long>? clock = null)
    {
        // Clamped well inside tick-arithmetic range so "fresh" comparisons can
        // never overflow even for absurd ttl values.
        _ttlMilliseconds = Math.Clamp(
            (long)(ttl ?? DefaultTtl).TotalMilliseconds, 0, long.MaxValue / 4);
        _clock = clock ?? DefaultClock;
    }

    /// <summary>
    /// The cached verdict, or null when nothing was ever recorded (or what was
    /// has gone stale). Callers render with an assumption in that case and let
    /// the next background probe correct them.
    /// </summary>
    public FileVerdict? Lookup(string path)
    {
        if (!_slots.TryGetValue(path, out var slot) || !IsFresh(slot))
        {
            return null;
        }

        return new FileVerdict(slot.Exists, slot.IsDirectory);
    }

    /// <summary>
    /// Whether scheduling a background probe for this path would teach the
    /// cache something: no verdict yet, or one that has gone stale. A fresh
    /// verdict needs nothing.
    /// </summary>
    public bool WantsProbe(string path)
        => !_slots.TryGetValue(path, out var slot) || !IsFresh(slot);

    /// <summary>Stores a probe's finding, fresh from now on.</summary>
    public void Record(string path, bool exists, bool isDirectory)
        => _slots[path] = new Slot(exists, isDirectory, _clock());

    /// <summary>
    /// Throws a verdict away so the next render probes again — for the moments
    /// the application itself knows the world moved (a save, a delete it
    /// performed).
    /// </summary>
    public void Invalidate(string path) => _slots.TryRemove(path, out _);

    private bool IsFresh(Slot slot)
    {
        // The injected clock is monotonic in every test and production use;
        // the guard keeps a clock that jumps backwards from expiring
        // everything catastrophically.
        var age = _clock() - slot.RecordedAt;
        return age >= 0 && age <= _ttlMilliseconds;
    }
}
