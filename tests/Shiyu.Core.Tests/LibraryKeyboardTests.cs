using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// 管理窗的 Esc 与窄条同构（票 24 / UI 报告 §5.2）：逐层剥，剥完为止——
/// 但最后一层不关窗（常驻工具窗，关窗是明确动作），这条与窄条的差异
/// 值得钉死，免得日后被"顺手统一"掉。
/// </summary>
public class LibraryKeyboardTests
{
    [Fact]
    public void Escape_peels_preview_then_query_then_filters_one_at_a_time()
    {
        Assert.Equal(LibraryEscapeAction.ClosePreview,
            LibraryKeyboard.NextEscape(previewOpen: true, hasQuery: true, filterCount: 3));
        Assert.Equal(LibraryEscapeAction.ClearQuery,
            LibraryKeyboard.NextEscape(previewOpen: false, hasQuery: true, filterCount: 3));
        Assert.Equal(LibraryEscapeAction.PopNewestFilter,
            LibraryKeyboard.NextEscape(previewOpen: false, hasQuery: false, filterCount: 3));
        Assert.Equal(LibraryEscapeAction.PopNewestFilter,
            LibraryKeyboard.NextEscape(previewOpen: false, hasQuery: false, filterCount: 1));
    }

    [Fact]
    public void Escape_never_closes_the_window()
    {
        // 最后一层：没有可剥的就停住。窄条在这里 HideWindow，管理窗不是。
        Assert.Equal(LibraryEscapeAction.Stay,
            LibraryKeyboard.NextEscape(previewOpen: false, hasQuery: false, filterCount: 0));
    }
}
