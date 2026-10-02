using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class BarRefreshPolicyTests
{
    // --- showing and hiding -------------------------------------------------------

    [Fact]
    public void Showing_reads_the_world_as_it_is_now()
    {
        var policy = new BarRefreshPolicy(lightweightWhenHidden: true);

        Assert.Equal(BarRefreshCommand.Reload, policy.Shown());
        Assert.True(policy.IsVisible);
    }

    [Theory]
    [InlineData(BarHideReason.Toggled)]
    [InlineData(BarHideReason.Pasted)]
    public void Hiding_runs_the_full_teardown_whatever_the_reason(BarHideReason reason)
    {
        // 票 14 的教训变成受测不变式：粘贴收起与热键收起同待遇，没有捷径。
        var policy = new BarRefreshPolicy(lightweightWhenHidden: true);
        policy.Shown();

        Assert.Equal(BarRefreshCommand.EnterLightweight, policy.Hidden(reason));
        Assert.False(policy.IsVisible);
    }

    [Fact]
    public void Hiding_without_the_lightweight_setting_hides_only()
    {
        var policy = new BarRefreshPolicy(lightweightWhenHidden: false);
        policy.Shown();

        Assert.Equal(BarRefreshCommand.None, policy.Hidden(BarHideReason.Toggled));
    }

    [Fact]
    public void Hiding_an_already_hidden_bar_does_nothing()
    {
        var policy = new BarRefreshPolicy(lightweightWhenHidden: true);
        policy.Shown();
        policy.Hidden(BarHideReason.Toggled);

        Assert.Equal(BarRefreshCommand.None, policy.Hidden(BarHideReason.Pasted));
    }

    [Fact]
    public void The_lightweight_setting_applies_from_the_next_hide()
    {
        var policy = new BarRefreshPolicy(lightweightWhenHidden: true);
        policy.Shown();
        policy.Hidden(BarHideReason.Toggled);

        policy.ApplySettings(lightweightWhenHidden: false);
        policy.Shown();
        Assert.Equal(BarRefreshCommand.None, policy.Hidden(BarHideReason.Toggled));

        policy.ApplySettings(lightweightWhenHidden: true);
        policy.Shown();
        Assert.Equal(BarRefreshCommand.EnterLightweight, policy.Hidden(BarHideReason.Toggled));
    }

    // --- store changes ---------------------------------------------------------------

    [Fact]
    public void A_change_while_visible_from_elsewhere_reloads()
    {
        var policy = new BarRefreshPolicy(lightweightWhenHidden: true);
        policy.Shown();

        Assert.Equal(BarRefreshCommand.Reload, policy.StoreChanged(selfWrite: false));
    }

    [Fact]
    public void A_change_while_hidden_is_ignored()
    {
        var policy = new BarRefreshPolicy(lightweightWhenHidden: true);
        policy.Shown();
        policy.Hidden(BarHideReason.Toggled);

        // Whatever changed while hidden is read in one go by the next Shown.
        Assert.Equal(BarRefreshCommand.None, policy.StoreChanged(selfWrite: false));
    }

    [Fact]
    public void The_windows_own_change_is_ignored()
    {
        var policy = new BarRefreshPolicy(lightweightWhenHidden: true);
        policy.Shown();

        // Favourite, note and pin already produced their visual in place; a
        // reload here would only reset the scroll under the user (O-37).
        Assert.Equal(BarRefreshCommand.None, policy.StoreChanged(selfWrite: true));
    }

    [Fact]
    public void Every_external_change_means_exactly_one_reload_no_more()
    {
        var policy = new BarRefreshPolicy(lightweightWhenHidden: true);
        policy.Shown();

        // A burst of Changed events (a copy plus its Touch) reloads once per
        // event — never twice for one event, never deferred and replayed in a
        // batch after hiding.
        Assert.Equal(BarRefreshCommand.Reload, policy.StoreChanged(selfWrite: false));
        Assert.Equal(BarRefreshCommand.Reload, policy.StoreChanged(selfWrite: false));

        policy.Hidden(BarHideReason.Toggled);
        Assert.Equal(BarRefreshCommand.None, policy.StoreChanged(selfWrite: false));

        // The changes ignored while hidden surface as the Shown reload, not
        // as replayed reloads.
        Assert.Equal(BarRefreshCommand.Reload, policy.Shown());
    }
}
