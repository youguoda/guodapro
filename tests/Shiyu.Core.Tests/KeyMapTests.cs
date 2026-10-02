using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// 键位即数据（§5.2、O-42）：表本身的完整性、与 HotkeyPlan 四键的一致、
/// 以及每个渲染函数的输出——「改一个键七处同步变」的验收在这里钉住：
/// 渲染文本全部由表与设置推导，任何一处手写都是测试要抓的漂移。
/// </summary>
public class KeyMapTests
{
    [Fact]
    public void Every_row_has_a_key_on_at_least_one_surface()
    {
        foreach (var row in KeyMap.Rows)
        {
            Assert.True(
                !string.IsNullOrWhiteSpace(row.Bar) || !string.IsNullOrWhiteSpace(row.Settings),
                $"{row.Id} has no key on any surface — a row without a key teaches nothing");
        }
    }

    [Fact]
    public void Row_ids_are_unique()
    {
        var ids = KeyMap.Rows.Select(row => row.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void A_badge_only_extends_a_row_that_has_a_bar_key()
    {
        foreach (var row in KeyMap.Rows.Where(row => row.Badge is not null))
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Bar), $"{row.Id} badges without a bar key");
        }
    }

    [Fact]
    public void Tray_key_letters_are_single_capitals_or_full_modifiers()
    {
        // 键帽有固定的高度与最小宽度：单字母帽与 Alt+S 这样的完整修饰键都
        // 放得下，但不能出现 "Ctrl+F" 这种带功能键全文的帽——那是速查行的事。
        foreach (var actionId in new[] { "copy", "open", "pin", "favorite", "note", "group", "delete" })
        {
            var key = KeyMap.TrayKey(actionId);
            Assert.NotNull(key);
            Assert.True(
                key!.Length is 1 or 2 or 3 or 4,
                $"{actionId} keycap text {key} is not a badge");
        }
    }

    [Fact]
    public void Tray_key_answers_null_for_actions_without_a_letter()
    {
        Assert.Null(KeyMap.TrayKey("paste"));
        Assert.Null(KeyMap.TrayKey("plain"));
        Assert.Null(KeyMap.TrayKey("locate"));
        Assert.Null(KeyMap.TrayKey("more"));
    }

    [Fact]
    public void Tray_key_reads_the_same_letters_the_bar_handler_types()
    {
        // 与 BarKeyHandling 的键盘分支一一对应（处理在 App 层测不到，这里
        // 把字母钉死：改表必读测试，改键盘必看表）。
        Assert.Equal("C", KeyMap.TrayKey("copy"));
        Assert.Equal("O", KeyMap.TrayKey("open"));
        Assert.Equal("P", KeyMap.TrayKey("pin"));
        Assert.Equal("S", KeyMap.TrayKey("favorite"));
        Assert.Equal("N", KeyMap.TrayKey("note"));
        Assert.Equal("G", KeyMap.TrayKey("group"));
        Assert.Equal("D", KeyMap.TrayKey("delete"));
    }

    [Fact]
    public void Badge_text_prefers_the_short_form_over_the_full_key()
    {
        // 搜索的完整键是 Ctrl+F；帽浮出时 Ctrl 正被按住，帽上只写 F。
        Assert.Equal("F", KeyMap.BadgeText("search"));
        Assert.Equal("Alt+S", KeyMap.BadgeText("favorite-filter"));
        Assert.Equal("Tab", KeyMap.BadgeText("tag"));
        Assert.Null(KeyMap.BadgeText("no-such-row"));
    }

    [Fact]
    public void Global_combinations_read_the_live_settings_for_every_action()
    {
        var settings = new AppSettings
        {
            CaptureHotkey = "Ctrl+Alt+Q",
            QuickBarHotkey = "Ctrl+Alt+P",
            BarHotkey = "Ctrl+Alt+B",
            ClipboardTranslateHotkey = "Ctrl+Alt+X",
            LibraryHotkey = "Ctrl+Alt+L",
        };

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var key = KeyMap.Combination(action, settings);
            Assert.NotNull(key);
            Assert.Contains("Ctrl+Alt+", key);
        }
    }

    [Fact]
    public void An_unset_combination_is_null_not_an_empty_string()
    {
        Assert.Null(KeyMap.Combination(HotkeyAction.Library, new AppSettings()));
    }

    [Fact]
    public void Default_combinations_stay_in_step_with_hotkey_plan()
    {
        // KeyMap 读的是设置属性名，HotkeyPlan 注册的也是同一批属性——两处
        // 各自列举动作，此测试保证列举对得上：默认方案里的每个绑定，KeyMap
        // 都给得出同一个组合；默认不设的（管理窗），两边都给空。
        var defaults = new AppSettings();
        var (bindings, problems) = HotkeyPlan.Build(defaults);

        Assert.Empty(problems);

        var byAction = bindings.ToDictionary(binding => binding.Action, binding => binding.Spec.ToString());
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var fromMap = KeyMap.Combination(action, defaults);
            if (byAction.TryGetValue(action, out var registered))
            {
                Assert.Equal(registered, fromMap);
            }
            else
            {
                Assert.Null(fromMap);
            }
        }
    }

    [Fact]
    public void Tray_menu_lines_carry_the_live_combination_in_the_accelerator_column()
    {
        // Win32 菜单惯例：\t 之后的文本右对齐成加速键列（§5.2）。
        Assert.Equal("打开窄条\tCtrl+Shift+B", KeyMap.TrayMenuLine(HotkeyAction.Bar, new AppSettings()));
        Assert.Equal("快速粘贴\tCtrl+Shift+V", KeyMap.TrayMenuLine(HotkeyAction.QuickBar, new AppSettings()));
        Assert.Equal("翻译剪贴板\tCtrl+Shift+X",
            KeyMap.TrayMenuLine(HotkeyAction.ClipboardTranslate, new AppSettings()));
    }

    [Fact]
    public void A_menu_row_without_a_hotkey_has_no_accelerator_column()
    {
        // 打开管理窗默认不设：行文里连 \t 都不出现，空列比没列诚实。
        Assert.Equal("管理历史…", KeyMap.TrayMenuLine(HotkeyAction.Library, new AppSettings()));
    }

    [Fact]
    public void Tray_menu_covers_every_hotkey_action_with_a_plain_label()
    {
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var line = KeyMap.TrayMenuLine(action, new AppSettings());
            var label = line.Split('\t')[0];
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.Contains(label, line);
        }
    }

    [Fact]
    public void Chips_split_a_combination_into_keycaps()
    {
        Assert.Equal(new[] { "Ctrl", "Shift", "B" }, KeyMap.Chips("Ctrl+Shift+B"));
        Assert.Equal(new[] { "Alt", "S" }, KeyMap.Chips("Alt+S"));
    }

    [Fact]
    public void Chips_of_an_unset_combination_is_an_empty_list()
    {
        Assert.Empty(KeyMap.Chips(null));
        Assert.Empty(KeyMap.Chips("  "));
    }

    [Fact]
    public void Cheat_sheet_lines_snapshot_the_rendered_table()
    {
        // 速查区从这行文本渲染；快照钉住格式与内容，改表必改这里。
        Assert.Equal(
        [
            "搜索：窄条 Ctrl+F · 设置 Ctrl+F",
            "粘贴（主动作）：窄条 Enter（对选中的卡片）",
            "打开搜索结果（主动作）：设置 Enter（搜索有结果时）",
            "复制：窄条 C",
            "打开：窄条 O",
            "置顶：窄条 P",
            "收藏：窄条 S",
            "备注：窄条 N",
            "归组：窄条 G",
            "删除：窄条 D",
            "撤销：窄条 Z（删除后的片刻内）",
            "预览全文：窄条 按住 Space",
            "编号直达：窄条 1–9、0（列表前 10 行）",
            "切换类型：窄条 ←→",
            "只看收藏：窄条 Alt+S",
            "循环标签：窄条 Tab",
            "显示键帽：窄条 按住 Ctrl · 设置 按住 Ctrl",
            "键位速查：设置 ? 或 F1",
            "切换设置页：设置 Ctrl+1–6",
            "分层退出：窄条 Esc",
        ],
            KeyMap.CheatSheetLines());
    }

    [Fact]
    public void The_same_verb_keeps_one_key_across_windows()
    {
        // §5.2 一套动词跨窗同键：搜索在两窗都是 Ctrl+F。
        var search = KeyMap.Find("search")!;
        Assert.Equal(search.Bar, search.Settings);
    }
}
