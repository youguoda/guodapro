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

        ClearTagFilter,

        ClearTypeFilter,

        HideWindow,
    }

    /// <summary>
    /// Escape undoes the most recent step, not the whole session: preview
    /// first, then the group filter, then the type filter, and only when
    /// nothing is left does it hide the window.
    /// </summary>
    public static EscapeAction NextEscape(bool previewOpen, bool hasTagFilter, bool hasTypeFilter)
    {
        if (previewOpen)
        {
            return EscapeAction.ClosePreview;
        }

        if (hasTagFilter)
        {
            return EscapeAction.ClearTagFilter;
        }

        if (hasTypeFilter)
        {
            return EscapeAction.ClearTypeFilter;
        }

        return EscapeAction.HideWindow;
    }

    /// <summary>Cycles an index through a list of choices, wrapping at both ends.</summary>
    public static int Cycle(int current, int delta, int count)
        => count <= 1 ? current : ((current + delta) % count + count) % count;
}
