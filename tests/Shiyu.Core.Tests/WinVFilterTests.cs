using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// A system key is being borrowed: the filter must swallow exactly Win+V and
/// its trailing V release, and let every other keystroke through untouched —
/// including the Win release itself, whose free passage is what keeps the
/// key from sticking system-wide (the Start menu is suppressed by an
/// injected mask keystroke instead, one layer up).
/// </summary>
public class WinVFilterTests
{
    private static KeyFlow Feed(WinVFilter filter, SpecialKey key, KeyDirection direction)
        => filter.Feed(key, direction);

    [Fact]
    public void Win_then_V_triggers_and_swallows_but_Win_release_passes()
    {
        var filter = new WinVFilter();
        var fired = 0;
        filter.Triggered += () => fired++;

        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(1, fired);

        // The V release is eaten (its down was eaten); the Win release PASSES
        // so the key never sticks — the Start menu is prevented by the mask
        // the hook injects, not by withholding the release.
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Up));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Up));
    }

    [Fact]
    public void Either_Win_works()
    {
        var filter = new WinVFilter();
        var fired = 0;
        filter.Triggered += () => fired++;

        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.RightWin, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Up));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.RightWin, KeyDirection.Up));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Plain_typing_passes_completely_even_after_a_takeover()
    {
        var filter = new WinVFilter();

        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.V, KeyDirection.Up));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.Other, KeyDirection.Down));

        // After one takeover, a later plain V press must not lose its release
        // to a stale takeover flag.
        Feed(filter, SpecialKey.LeftWin, KeyDirection.Down);
        Feed(filter, SpecialKey.V, KeyDirection.Down);
        Feed(filter, SpecialKey.V, KeyDirection.Up);
        Feed(filter, SpecialKey.LeftWin, KeyDirection.Up);

        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.V, KeyDirection.Up));
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
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.V, KeyDirection.Up));
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Held_repeats_swallow_without_retrigger_but_a_real_second_press_triggers()
    {
        var filter = new WinVFilter();
        var fired = 0;
        filter.Triggered += () => fired++;

        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Down));

        // Auto-repeat: down after down, no release between — swallowed, no
        // re-trigger (auto-repeat must not toggle the bar).
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(1, fired);
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Up));

        // A genuine second press — full down/up pair — triggers again: the
        // bar toggles, and nothing reaches the system.
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Down));
        Assert.Equal(2, fired);
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Up));
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Up));
    }

    [Fact]
    public void V_released_after_Win_still_gets_its_up_eaten_once()
    {
        var filter = new WinVFilter();

        Feed(filter, SpecialKey.LeftWin, KeyDirection.Down);
        Feed(filter, SpecialKey.V, KeyDirection.Down);
        // Fast fingers: Win released before V.
        Assert.Equal(KeyFlow.Pass, Feed(filter, SpecialKey.LeftWin, KeyDirection.Up));
        Assert.Equal(KeyFlow.Swallow, Feed(filter, SpecialKey.V, KeyDirection.Up));
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
