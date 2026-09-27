namespace Shiyu.Core;

/// <summary>
/// The narrow bar's keyboard decisions. In Core because the rules are the
/// product: which layer Escape peels next, how filters cycle. The window
/// supplies the state; this decides what that state means.
/// </summary>
public static class BarKeyboard
{
    public enum EscapeAction
    {
        /// <summary>The innermost layer: whatever preview is open goes away.</summary>
        ClosePreview,

        /// <summary>The search query empties, list returns to full.</summary>
        ClearQuery,

        ClearSubtypeFilter,

        ClearTagFilter,

        ClearTypeFilter,

        /// <summary>The ★-only toggle turns off.</summary>
        ClearFavoriteFilter,

        ClearGroupFilter,

        HideWindow,
    }

    /// <summary>
    /// Escape undoes the most recent step, not the whole session: the preview,
    /// then each filter layer from most-recently-touched to coarsest (query,
    /// subtype, tag, type, favourite, group), and only when every layer is
    /// clear does it hide the window. The footer's "Esc …" hint reads the same
    /// stack, so what the text promises is what the key does.
    /// </summary>
    public static EscapeAction NextEscape(
        bool previewOpen,
        bool hasQuery,
        bool hasSubtypeFilter,
        bool hasTagFilter,
        bool hasTypeFilter,
        bool hasFavoriteFilter,
        bool hasGroupFilter)
    {
        if (previewOpen)
        {
            return EscapeAction.ClosePreview;
        }

        if (hasQuery)
        {
            return EscapeAction.ClearQuery;
        }

        if (hasSubtypeFilter)
        {
            return EscapeAction.ClearSubtypeFilter;
        }

        if (hasTagFilter)
        {
            return EscapeAction.ClearTagFilter;
        }

        if (hasTypeFilter)
        {
            return EscapeAction.ClearTypeFilter;
        }

        if (hasFavoriteFilter)
        {
            return EscapeAction.ClearFavoriteFilter;
        }

        if (hasGroupFilter)
        {
            return EscapeAction.ClearGroupFilter;
        }

        return EscapeAction.HideWindow;
    }

    /// <summary>Cycles an index through a list of choices, wrapping at both ends.</summary>
    public static int Cycle(int current, int delta, int count)
        => count <= 1 ? current : ((current + delta) % count + count) % count;
}
