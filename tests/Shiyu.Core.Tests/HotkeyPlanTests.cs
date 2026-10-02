using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class HotkeyPlanTests
{
    private static AppSettings Settings(
        string capture = "Ctrl+Shift+Z",
        string quickBar = "Ctrl+Shift+V",
        string bar = "Ctrl+Shift+B",
        string clipboard = "Ctrl+Shift+X")
        => new()
        {
            CaptureHotkey = capture,
            QuickBarHotkey = quickBar,
            BarHotkey = bar,
            ClipboardTranslateHotkey = clipboard,
        };

    // --- the good path ----------------------------------------------------------

    [Fact]
    public void Four_distinct_hotkeys_build_four_bindings_and_no_problems()
    {
        var (bindings, problems) = HotkeyPlan.Build(Settings());

        Assert.Empty(problems);
        // Registration order, so a collision elsewhere would still resolve the
        // way the App has always resolved it.
        Assert.Equal(
            [
                HotkeyAction.CaptureSelection,
                HotkeyAction.QuickBar,
                HotkeyAction.Bar,
                HotkeyAction.ClipboardTranslate,
            ],
            bindings.Select(b => b.Action));
        Assert.Equal("Ctrl+Shift+Z", bindings[0].Spec.ToString());
    }

    // --- collisions: every pair named -------------------------------------------

    [Theory]
    [InlineData(HotkeyAction.CaptureSelection, HotkeyAction.QuickBar)]
    [InlineData(HotkeyAction.CaptureSelection, HotkeyAction.Bar)]
    [InlineData(HotkeyAction.CaptureSelection, HotkeyAction.ClipboardTranslate)]
    [InlineData(HotkeyAction.QuickBar, HotkeyAction.Bar)]
    [InlineData(HotkeyAction.QuickBar, HotkeyAction.ClipboardTranslate)]
    [InlineData(HotkeyAction.Bar, HotkeyAction.ClipboardTranslate)]
    public void A_collision_names_both_actions(HotkeyAction a, HotkeyAction b)
    {
        // Every action gets its own distinct key except the colliding pair,
        // which shares Ctrl+Alt+K.
        var keyed = new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.CaptureSelection] = "Ctrl+Alt+1",
            [HotkeyAction.QuickBar] = "Ctrl+Alt+2",
            [HotkeyAction.Bar] = "Ctrl+Alt+3",
            [HotkeyAction.ClipboardTranslate] = "Ctrl+Alt+4",
            [a] = "Ctrl+Alt+K",
            [b] = "Ctrl+Alt+K",
        };

        var (bindings, problems) = HotkeyPlan.Build(Settings(
            keyed[HotkeyAction.CaptureSelection],
            keyed[HotkeyAction.QuickBar],
            keyed[HotkeyAction.Bar],
            keyed[HotkeyAction.ClipboardTranslate]));

        var problem = Assert.Single(problems);
        Assert.Contains(HotkeyPlan.ActionNames[a], problem);
        Assert.Contains(HotkeyPlan.ActionNames[b], problem);
        Assert.Contains("Ctrl+Alt+K", problem);

        // First registered wins the key; the loser is left out (registered
        // elsewhere it would only fail at RegisterHotKey and get reported a
        // second time, as a misleading "taken by other software").
        var order = new[] { HotkeyAction.CaptureSelection, HotkeyAction.QuickBar, HotkeyAction.Bar, HotkeyAction.ClipboardTranslate };
        var loser = Array.IndexOf(order, a) > Array.IndexOf(order, b) ? a : b;
        Assert.Equal(3, bindings.Count);
        Assert.DoesNotContain(bindings, x => x.Action == loser);
    }

    [Fact]
    public void Three_keys_sharing_one_combination_report_three_pairwise_problems_and_keep_one()
    {
        var (bindings, problems) = HotkeyPlan.Build(Settings(
            capture: "Ctrl+Alt+K", quickBar: "Ctrl+Alt+K", bar: "Ctrl+Alt+K"));

        // C(3,2) pairs, one problem each — every pair is named.
        Assert.Equal(3, problems.Count);
        Assert.All(problems, p => Assert.Contains("Ctrl+Alt+K", p));

        // First registered keeps the key; the other two are left out rather
        // than registered to fail. The untouched fourth key survives.
        var survivor = Assert.Single(bindings, b => b.Spec.ToString() == "Ctrl+Alt+K");
        Assert.Equal(HotkeyAction.CaptureSelection, survivor.Action);
        Assert.Equal(2, bindings.Count);
    }

    // --- the onboarding regression ------------------------------------------------

    [Fact]
    public void Setting_the_bar_hotkey_to_the_quick_bar_default_is_reported_as_a_collision()
    {
        // The guide edits three hotkeys and leaves QuickBar at its default
        // (Ctrl+Shift+V). Its old validation only compared the three edited
        // keys, so this sailed through and the App's registration then failed
        // with a false "taken by other software". The plan checks all four.
        var (bindings, problems) = HotkeyPlan.Build(Settings(bar: "Ctrl+Shift+V"));

        var problem = Assert.Single(problems);
        Assert.Contains(HotkeyPlan.ActionNames[HotkeyAction.QuickBar], problem);
        Assert.Contains(HotkeyPlan.ActionNames[HotkeyAction.Bar], problem);

        // First registered wins, as before: the quick bar keeps the key.
        Assert.DoesNotContain(bindings, b => b.Action == HotkeyAction.Bar);
        Assert.Contains(bindings, b => b.Action == HotkeyAction.QuickBar);
    }

    // --- unreadable input ----------------------------------------------------------

    [Fact]
    public void A_bare_key_without_modifiers_is_rejected_in_plain_words()
    {
        var (bindings, problems) = HotkeyPlan.Build(Settings(capture: "Z"));

        Assert.Empty(bindings.Where(b => b.Action == HotkeyAction.CaptureSelection));
        var problem = Assert.Single(problems);
        Assert.Contains(HotkeyPlan.ActionNames[HotkeyAction.CaptureSelection], problem);
        Assert.Contains("至少带一个修饰键", problem);
    }

    [Fact]
    public void Unreadable_text_costs_one_hotkey_not_the_set()
    {
        var (bindings, problems) = HotkeyPlan.Build(Settings(clipboard: "Ctrl++"));

        var problem = Assert.Single(problems);
        Assert.Contains(HotkeyPlan.ActionNames[HotkeyAction.ClipboardTranslate], problem);

        Assert.Equal(3, bindings.Count);
        Assert.DoesNotContain(bindings, b => b.Action == HotkeyAction.ClipboardTranslate);
    }
}
