namespace Shiyu.Core;

/// <summary>
/// The library window's Escape layers（票 24 / UI 报告 §5.2）.
/// </summary>
public enum LibraryEscapeAction
{
    /// <summary>The innermost layer: the fullscreen preview goes away.</summary>
    ClosePreview,

    /// <summary>The search query empties, list returns to full.</summary>
    ClearQuery,

    /// <summary>The most recently added filter token pops, one at a time.</summary>
    PopNewestFilter,

    /// <summary>
    /// 最后一层：Esc 到此为止，不关窗。管理窗是常驻的工具窗，关窗是
    /// 明确动作（Ctrl+W 或系统 caption 的 ✕），误触 Esc 不该付出它。
    /// </summary>
    Stay,
}

/// <summary>
/// The library window's keyboard decisions. In Core for the same reason as
/// <see cref="BarKeyboard"/>: which layer Escape peels next is product rule,
/// not window plumbing, and it belongs beside its tests. The window supplies
/// the state; this decides what that state means.
///
/// 键位本身的单一来源（KeyMap，七个渲染面同表）属票 25；本类只收 Escape
/// 的分层决策，管理窗内的键位速查表先在窗内静态声明并等票 25 收编。
/// </summary>
public static class LibraryKeyboard
{
    /// <summary>
    /// Escape peels the most recent layer: the preview, then the query, then
    /// the filter tokens one at a time (most recently added first). When every
    /// layer is clear Esc does nothing — the window stays.
    /// </summary>
    public static LibraryEscapeAction NextEscape(bool previewOpen, bool hasQuery, int filterCount)
    {
        if (previewOpen)
        {
            return LibraryEscapeAction.ClosePreview;
        }

        if (hasQuery)
        {
            return LibraryEscapeAction.ClearQuery;
        }

        if (filterCount > 0)
        {
            return LibraryEscapeAction.PopNewestFilter;
        }

        return LibraryEscapeAction.Stay;
    }
}
