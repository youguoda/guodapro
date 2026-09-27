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
    public void Escape_peels_every_filter_layer_before_hiding()
    {
        // The full stack: preview → query → subtype → tag → type → favourite
        // → group → hide. Every layer the footer can promise has a peel.
        Assert.Equal(BarKeyboard.EscapeAction.ClosePreview,
            BarKeyboard.NextEscape(previewOpen: true, hasQuery: true, hasSubtypeFilter: true,
                hasTagFilter: true, hasTypeFilter: true, hasFavoriteFilter: true, hasGroupFilter: true));
        Assert.Equal(BarKeyboard.EscapeAction.ClearQuery,
            BarKeyboard.NextEscape(previewOpen: false, hasQuery: true, hasSubtypeFilter: true,
                hasTagFilter: true, hasTypeFilter: true, hasFavoriteFilter: true, hasGroupFilter: true));
        Assert.Equal(BarKeyboard.EscapeAction.ClearSubtypeFilter,
            BarKeyboard.NextEscape(previewOpen: false, hasQuery: false, hasSubtypeFilter: true,
                hasTagFilter: true, hasTypeFilter: true, hasFavoriteFilter: true, hasGroupFilter: true));
        Assert.Equal(BarKeyboard.EscapeAction.ClearTagFilter,
            BarKeyboard.NextEscape(previewOpen: false, hasQuery: false, hasSubtypeFilter: false,
                hasTagFilter: true, hasTypeFilter: true, hasFavoriteFilter: true, hasGroupFilter: true));
        Assert.Equal(BarKeyboard.EscapeAction.ClearTypeFilter,
            BarKeyboard.NextEscape(previewOpen: false, hasQuery: false, hasSubtypeFilter: false,
                hasTagFilter: false, hasTypeFilter: true, hasFavoriteFilter: true, hasGroupFilter: true));
        Assert.Equal(BarKeyboard.EscapeAction.ClearFavoriteFilter,
            BarKeyboard.NextEscape(previewOpen: false, hasQuery: false, hasSubtypeFilter: false,
                hasTagFilter: false, hasTypeFilter: false, hasFavoriteFilter: true, hasGroupFilter: true));
        Assert.Equal(BarKeyboard.EscapeAction.ClearGroupFilter,
            BarKeyboard.NextEscape(previewOpen: false, hasQuery: false, hasSubtypeFilter: false,
                hasTagFilter: false, hasTypeFilter: false, hasFavoriteFilter: false, hasGroupFilter: true));
        Assert.Equal(BarKeyboard.EscapeAction.HideWindow,
            BarKeyboard.NextEscape(previewOpen: false, hasQuery: false, hasSubtypeFilter: false,
                hasTagFilter: false, hasTypeFilter: false, hasFavoriteFilter: false, hasGroupFilter: false));
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
