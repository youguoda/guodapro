namespace Shiyu.Core;

/// <summary>
/// Walks the history a page at a time, optionally narrowed by a search.
///
/// Lives here rather than in the window so the awkward part — knowing how far
/// it has read, whether there is more, and what a new search invalidates — is
/// testable without a user interface.
/// </summary>
public sealed class HistoryBrowser(EntryStore store, int pageSize = 100)
{
    private readonly List<Entry> _loaded = [];
    private string _query = string.Empty;

    /// <summary>What has been read so far, newest first.</summary>
    public IReadOnlyList<Entry> Loaded => _loaded;

    /// <summary>False once a page came back short, meaning the end was reached.</summary>
    public bool HasMore { get; private set; } = true;

    /// <summary>
    /// The current search. Blank browses the whole history. Assigning starts
    /// again from the top: results for the previous query mean nothing now.
    /// </summary>
    public string Query
    {
        get => _query;
        set
        {
            var incoming = value ?? string.Empty;
            if (incoming == _query)
            {
                return;
            }

            _query = incoming;
            Reset();
        }
    }

    /// <summary>Discards what was read and loads the first page again.</summary>
    public void Reset()
    {
        _loaded.Clear();
        HasMore = true;
        LoadMore();
    }

    /// <summary>Reads the next page, returning how many entries it added.</summary>
    public int LoadMore()
    {
        if (!HasMore)
        {
            return 0;
        }

        var page = string.IsNullOrWhiteSpace(_query)
            ? store.Page(pageSize, _loaded.Count)
            : store.Search(_query, pageSize, _loaded.Count);

        _loaded.AddRange(page);

        // A short page means the end; a full one might still be the last, which
        // the next call discovers by coming back empty.
        HasMore = page.Count == pageSize;
        return page.Count;
    }

    /// <summary>
    /// Forgets one entry without re-reading everything, so deleting from a long
    /// list does not send the user back to the top.
    /// </summary>
    public void Forget(long id) => _loaded.RemoveAll(entry => entry.Id == id);
}
