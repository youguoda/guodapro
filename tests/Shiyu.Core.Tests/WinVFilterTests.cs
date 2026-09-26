using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// A system key is being borrowed: the filter must swallow exactly Win+V and
/// its trailing releases, and let every other keystroke through untouched —
/// plain typing, Win alone, and other Win combinations included.
/// </summary>
public class WinVFilterTests
{
    private static KeyFlow Feed(WinVFilter filter, SpecialKey key, KeyDirection direction)
        => filter.Feed(key, direction);

    [Fact]
    public void Win_then_V_triggers_and_swallows()
    {
        var filter = new WinVFilter();
        var fired = 0;
        filter.Triggered += () => fired++;

        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(1, fired);

        // The takeover's tail: both releases eaten so Start never opens.
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Up));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.LeftWin, KeyDirection.Up));
    }

    [Fact]
    public void Either_Win_works()
    {
        var filter = new WinVFilter();
        var fired = 0;
        filter.Triggered += () => fired++;

        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.RightWin, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.RightWin, KeyDirection.Up));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Plain_typing_passes_completely()
    {
        var filter = new WinVFilter();

        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.V, KeyDirection.Up));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.Other, KeyDirection.Down));
    }

    [Fact]
    public void Win_alone_and_other_win_combos_pass()
    {
        var filter = new WinVFilter();

        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Down));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.Other, KeyDirection.Down));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.Other, KeyDirection.Up));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Up));

        // Win+R, Win+E: identical shape to Win+V but a different key.
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Down));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.Other, KeyDirection.Down));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Up));
    }

    [Fact]
    public void Win_released_before_V_means_no_takeover()
    {
        var filter = new WinVFilter();
        var fired = 0;
        filter.Triggered += () => fired++;

        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Down));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Up));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Second_V_while_holding_is_swallowed_without_retrigger()
    {
        var filter = new WinVFilter();
        var fired = 0;
        filter.Triggered += () => fired++;

        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Up));

        // Win still held, V pressed again: swallowed (handing it to the
        // system would pop the Windows clipboard panel), but no re-trigger
        // (auto-repeat must not toggle the bar).
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Up));
        Assert.Equal(1, fired);

        // The tail is still eaten, and the filter comes out clean.
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.LeftWin, KeyDirection.Up));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Down));
    }

    [Fact]
    public void After_a_full_takeover_the_next_win_combo_behaves_normally()
    {
        var filter = new WinVFilter();

        Feed(filter, SpecialKey.LeftWin, KeyDirection.Down);
        Feed(filter, SpecialKey.V, KeyDirection.Down);
        Feed(filter, SpecialKey.V, KeyDirection.Up);
        Feed(filter, SpecialKey.LeftWin, KeyDirection.Up);

        // A later Win+V triggers again — the takeover did not jam the filter.
        var fired = 0;
        filter.Triggered += () => fired++;
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.RightWin, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(1, fired);
    }
}
