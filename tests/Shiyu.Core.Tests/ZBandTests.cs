using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// The z-band policy (票 39 关置顶降级): a surface bound to the bar lands in
/// the bar's own band, so a covered bar is never shadowed by its own floating
/// panes. Pinned here as the pure judgment; the Win32 layer only translates a
/// band into an insert-after handle.
/// </summary>
public class ZBandTests
{
    [Fact]
    public void A_pinned_bar_keeps_its_bound_layers_in_the_topmost_band()
    {
        Assert.Equal(ZBand.Topmost, ZBandPolicy.FollowsHost(hostPinned: true));
    }

    [Fact]
    public void An_unpinned_bar_lands_its_bound_layers_in_the_normal_band()
    {
        // The summon path and every preview landing read this one judgment:
        // unpinned means no topmost bit, on the bar itself and on the panes
        // anchored to it.
        Assert.Equal(ZBand.Normal, ZBandPolicy.FollowsHost(hostPinned: false));
    }
}
