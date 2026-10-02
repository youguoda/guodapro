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
    private HistoryFilter _filter = HistoryFilter.None;

    /// <summary>
    /// Where the last page ended. The next page continues strictly after it:
    /// copies that arrived meanwhile sit above the cursor and cannot repeat,
    /// where OFFSET paging would shift under them and read history twice or
    /// skip it entirely.
    /// </summary>
    private PageCursor? _cursor;

    /// <summary>What has been read so far, newest first.</summary>
    public IReadOnlyList<Entry> Loaded => _loaded;

    /// <summary>False once a page came back short, meaning the end was reached.</summary>
    public bool HasMore { get; private set; } = true;

    /// <summary>
    /// What the history is narrowed to. Assigning starts again from the top:
    /// results for the previous filter mean nothing now.
    /// </summary>
    public HistoryFilter Filter
    {
        get => _filter;
        set
        {
            var incoming = value ?? HistoryFilter.None;
            if (incoming == _filter)
            {
                return;
            }

            _filter = incoming;
            Reset();
        }
    }

    /// <summary>Shorthand for the keyword alone, leaving other parts as they are.</summary>
    public string Query
    {
        get => _filter.Query ?? string.Empty;
        set => Filter = _filter with { Query = value };
    }

    /// <summary>Discards what was read and loads the first page again.</summary>
    public void Reset()
    {
        _loaded.Clear();
        _cursor = null;
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

        var page = store.Find(_filter, pageSize, _cursor);

        _loaded.AddRange(page);

        // A short page means the end; a full one might still be the last, which
        // the next call discovers by coming back empty.
        HasMore = page.Count == pageSize;
        if (page.Count > 0)
        {
            _cursor = PageCursor.Of(page[^1]);
        }

        return page.Count;
    }

    /// <summary>
    /// Forgets one entry without re-reading everything, so deleting from a long
    /// list does not send the user back to the top.
    /// </summary>
    public void Forget(long id) => _loaded.RemoveAll(entry => entry.Id == id);
}
