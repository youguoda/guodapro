using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// The settings surface is data, so its integrity is testable as data: ids
/// unique, parents real toggles, numbers bounded, segmented choices present,
/// keywords there for the search that is coming — the checks that would
/// otherwise only surface as a broken page at runtime.
/// </summary>
public class SettingsSchemaTests
{
    private static IEnumerable<SettingsItem> Items()
        => SettingsSchema.Tree.SelectMany(page => page.Sections).SelectMany(section => section.Items);

    [Fact]
    public void Every_item_id_is_unique()
    {
        var ids = Items().Select(item => item.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void A_parent_exists_and_is_a_toggle()
    {
        var byId = Items().ToDictionary(item => item.Id);
        foreach (var child in Items().Where(item => item.Parent is not null))
        {
            var parent = byId.TryGetValue(child.Parent!, out var found) ? found : null;
            Assert.NotNull(parent);
            Assert.Equal(SettingsControl.Toggle, parent!.Control);
        }
    }

    [Fact]
    public void Numbers_are_bounded_sensibly()
    {
        foreach (var item in Items().Where(item => item.Control == SettingsControl.Number))
        {
            Assert.True(item.Min < item.Max, $"{item.Id} has no meaningful range");
        }
    }

    [Fact]
    public void Segmented_items_offer_at_least_two_choices()
    {
        foreach (var item in Items().Where(item => item.Control == SettingsControl.Segmented))
        {
            Assert.True(item.ChoiceList.Length >= 2, $"{item.Id} needs choices to be a segmented control");
        }
    }

    [Fact]
    public void Every_item_is_searchable_by_label_or_keyword()
    {
        foreach (var item in Items())
        {
            Assert.False(
                string.IsNullOrWhiteSpace(item.Label) && item.KeywordList.Length == 0,
                $"{item.Id} has neither label nor keywords");
        }
    }

    [Fact]
    public void Pages_carry_copy_that_names_user_intent()
    {
        var titles = SettingsSchema.Tree.Select(page => page.Title).ToList();
        Assert.Contains("快捷键", titles);
        Assert.Contains("数据", titles);
        Assert.DoesNotContain("常规", titles);
        Assert.DoesNotContain("高级", titles);
        Assert.All(SettingsSchema.Tree, page => Assert.NotEmpty(page.Sections));
    }

    [Fact]
    public void The_old_settings_surface_is_covered_by_the_tree()
    {
        var ids = Items().Select(item => item.Id).ToHashSet();

        Assert.Contains("theme", ids);
        Assert.Contains("hotkey.capture", ids);
        Assert.Contains("hotkey.clipboard", ids);
        Assert.Contains("hotkey.quickbar", ids);
        Assert.Contains("hotkey.bar", ids);
        Assert.Contains("bar.text-lines", ids);
        Assert.Contains("bar.image-height", ids);
        Assert.Contains("bar.file-count", ids);
        Assert.Contains("bar.actions", ids);
        Assert.Contains("action.sound", ids);
        Assert.Contains("service.target-language", ids);
        Assert.Contains("service.source-language", ids);
        Assert.Contains("service.base-url", ids);
        Assert.Contains("service.model", ids);
        Assert.Contains("service.api-key", ids);
        Assert.Contains("store.retention-days", ids);
        Assert.Contains("store.protect", ids);
        Assert.Contains("store.protect-favorites", ids);
        Assert.Contains("store.protect-pinned", ids);
        Assert.Contains("store.directory", ids);
        Assert.Contains("store.start-with-windows", ids);
        Assert.Contains("exclusions", ids);
    }

    [Fact]
    public void The_credential_item_is_a_password_and_never_a_plain_text_box()
    {
        var credential = Items().Single(item => item.Id == "service.api-key");
        Assert.Equal(SettingsControl.Password, credential.Control);
    }

    [Fact]
    public void The_theme_is_a_segmented_control_not_a_dropdown()
    {
        var theme = Items().Single(item => item.Id == "theme");
        Assert.Equal(SettingsControl.Segmented, theme.Control);
        Assert.Equal(3, theme.ChoiceList.Length);
    }

    [Fact]
    public void Delete_protection_children_collapse_under_the_master_toggle()
    {
        var favorites = Items().Single(item => item.Id == "store.protect-favorites");
        var pinned = Items().Single(item => item.Id == "store.protect-pinned");
        Assert.Equal("store.protect", favorites.Parent);
        Assert.Equal("store.protect", pinned.Parent);
    }

    [Fact]
    public void The_protection_master_is_on_by_default()
    {
        Assert.True(new AppSettings().ProtectEntries);
    }

    [Fact]
    public void The_bar_comes_up_beside_the_cursor_by_default()
    {
        Assert.True(new AppSettings().BarAtCursor);
        Assert.Contains(
            SettingsSchema.Tree.SelectMany(p => p.Sections).SelectMany(s => s.Items),
            item => item.Id == "bar.at-cursor" && item.Control == SettingsControl.Toggle);
    }

    [Fact]
    public void The_bar_is_pinned_to_the_top_by_default_and_the_setting_is_a_look_toggle()
    {
        // 置顶 is how the resident bar works by default; the switch that turns
        // it off belongs with the interface's appearance, not with behaviour
        // the user has no reason to look for (ticket 39).
        Assert.True(new AppSettings().BarAlwaysOnTop);

        var item = Items().Single(i => i.Id == "look.bar-topmost");
        Assert.Equal(SettingsControl.Toggle, item.Control);
        Assert.Equal("look", SettingsSchema.Tree.Single(p => p.Sections.SelectMany(s => s.Items).Contains(item)).Id);
    }

    [Fact]
    public void The_selection_badge_is_off_by_default_and_its_switch_lives_with_the_hotkeys()
    {
        // 默认关闭是票 37 的硬约束：全局鼠标钩子的开销不为用户决定。
        // 开关挂在"快捷键"页——它和划词热键是同一件事的两种触发方式。
        Assert.False(new AppSettings().SelectionBadge);

        var item = Items().Single(i => i.Id == "hotkeys.selection-badge");
        Assert.Equal(SettingsControl.Toggle, item.Control);
        Assert.Equal("hotkeys", SettingsSchema.Tree.Single(p => p.Sections.SelectMany(s => s.Items).Contains(item)).Id);
    }
}
