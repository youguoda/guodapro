using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// Escape is a stack of "undo my last step", not a close button — the
/// decision of which layer gives way next is the part worth pinning down,
/// because getting it wrong is what makes users afraid of the key.
/// </summary>
public class BarKeyboardTests
{
    [Fact]
    public void Escape_peels_the_most_recent_layer_first()
    {
        // Preview (innermost) → group filter → type filter → hide.
        Assert.Equal(BarKeyboard.EscapeAction.ClosePreview,
            BarKeyboard.NextEscape(previewOpen: true, hasTagFilter: true, hasTypeFilter: true));
        Assert.Equal(BarKeyboard.EscapeAction.ClearTagFilter,
            BarKeyboard.NextEscape(previewOpen: false, hasTagFilter: true, hasTypeFilter: true));
        Assert.Equal(BarKeyboard.EscapeAction.ClearTypeFilter,
            BarKeyboard.NextEscape(previewOpen: false, hasTagFilter: false, hasTypeFilter: true));
        Assert.Equal(BarKeyboard.EscapeAction.HideWindow,
            BarKeyboard.NextEscape(previewOpen: false, hasTagFilter: false, hasTypeFilter: false));
    }

    [Fact]
    public void Cycling_the_type_filter_wraps_around()
    {
        Assert.Equal(1, BarKeyboard.Cycle(0, delta: 1, count: 3));
        Assert.Equal(0, BarKeyboard.Cycle(2, delta: 1, count: 3));
        Assert.Equal(2, BarKeyboard.Cycle(0, delta: -1, count: 3));
    }

    [Fact]
    public void Cycling_a_single_choice_lands_where_it_started()
    {
        Assert.Equal(0, BarKeyboard.Cycle(0, delta: 1, count: 1));
    }
}
