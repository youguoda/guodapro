namespace Shiyu.Core;

/// <summary>
/// 键位表覆盖的窗口（§5.2「各窗口的按键」）。本表先接窄条与设置窗两列；
/// 管理窗的键位由票 24 自带，合入后由后续小票把它的静态表收编进同一列——
/// 收编之前这里没有它的列，免得出现一行谁也不渲染的假数据。
/// </summary>
public enum KeySurface
{
    /// <summary>窄条（BarWindow）：卡片动作、筛选行、教学键帽。</summary>
    Bar,

    /// <summary>设置窗（SettingsWindow）：搜索、导航、速查。</summary>
    Settings,

    /// <summary>
    /// 管理窗（LibraryWindow，票 27 起 KeyMap 有它的列）：与窄条一套动词，
    /// 两处有意不同——没有粘贴目标（Enter 是复制）、Tab 留给焦点导航（标签 T）。
    /// </summary>
    Library,
}

/// <summary>
/// 键位即数据的一行（§5.2）：动作 Id、名称、字形、各窗按键、适用条件——
/// 与 <see cref="SettingsSchema"/> 同构的数据，界面不改表就改不了键帽。
/// </summary>
/// <param name="Id">动作 Id。悬停托盘的动作与 <see cref="HoverActions"/> 同名（copy、pin…）。</param>
/// <param name="Glyph">Fluent 字形码位（"E8C8"），速查行左缘图标；可空。</param>
/// <param name="Bar">窄条里的按键（"C"、"Ctrl+F"、"按住 Space"）。</param>
/// <param name="Settings">设置窗里的按键。</param>
/// <param name="Library">管理窗的按键；与窄条同键的行省略（回落 Bar），只在真正不同的地方写（标签 T）。</param>
/// <param name="Badge">
/// 按住 Ctrl 时键帽的短形（默认取 <see cref="Bar"/> 原文）：搜索的完整键是
/// Ctrl+F，而键帽浮出时 Ctrl 正被按住，帽上只写 F。
/// </param>
/// <param name="Condition">按键成立的条件（"删除后的片刻内"），速查行如实转述。</param>
public sealed record KeyMapRow(
    string Id,
    string Name,
    string? Glyph = null,
    string? Bar = null,
    string? Settings = null,
    string? Library = null,
    string? Badge = null,
    string? Condition = null);

/// <summary>
/// 键位的单一来源（§5.2、O-42）：窗口内按键是一张静态表，全局热键按
/// <see cref="HotkeyAction"/> 从设置读现值。窄条键帽、卡片菜单加速键列、
/// 设置速查、托盘菜单加速键列、引导第 2 屏、L2 鼠标提示，全部从这渲染——
/// 改一个键，处处同时变，单测钉住这份一致。
/// </summary>
public static class KeyMap
{
    /// <summary>
    /// 窗口内按键表。字母行（copy=C…）与键盘处理（BarKeys/BarKeyHandling）
    /// 一一对应；跨窗同键的动词（搜索 Ctrl+F）一行两列并排可见。
    /// </summary>
    public static readonly IReadOnlyList<KeyMapRow> Rows =
    [
        // 一套动词，跨窗同键（§5.2 表 1）。
        new("search", "搜索", "E721", Bar: "Ctrl+F", Settings: "Ctrl+F", Badge: "F"),
        new("paste", "粘贴（主动作）", "E77F", Bar: "Enter", Condition: "对选中的卡片"),
        new("open-result", "打开搜索结果（主动作）", "E721", Settings: "Enter", Condition: "搜索有结果时"),

        // 窄条卡片动作：字母即悬停托盘键帽（HoverActions 同名）。
        new("copy", "复制", "E8C8", Bar: "C"),
        new("open", "打开", "E8E5", Bar: "O"),
        new("pin", "置顶", "E718", Bar: "P"),
        new("favorite", "收藏", "E735", Bar: "S"),
        new("note", "备注", "E70B", Bar: "N"),
        new("group", "归组", "E8B7", Bar: "G"),
        new("delete", "删除", "E74D", Bar: "D"),
        new("undo", "撤销", "E10E", Bar: "Z", Condition: "删除后的片刻内"),
        new("preview", "预览全文", "E823", Bar: "按住 Space"),
        new("numbered", "编号直达", null, Bar: "1–9、0", Condition: "列表前 10 行"),

        // 窄条筛选行。标签在管理窗是 T（Tab 留给焦点导航，§5.2），动作同名
        // 同键——名字只留一个（"标签"），两窗的自动化名都从这里出。
        new("kind", "切换类型", "E8A9", Bar: "←→"),
        new("favorite-filter", "只看收藏", "E734", Bar: "Alt+S"),
        new("tag", "标签", "E8EC", Bar: "Tab", Library: "T"),

        // 两窗共用的教学与速查动作（§5.2 四级教学 L1）。
        new("hints", "显示键帽", "E765", Bar: "按住 Ctrl", Settings: "按住 Ctrl"),
        new("cheatsheet", "键位速查", "E765", Settings: "? 或 F1"),
        new("nav", "切换设置页", null, Settings: "Ctrl+1–6"),
        new("escape", "分层退出", null, Bar: "Esc"),
    ];

    /// <summary>托盘菜单里全局动作的行文（§5.2 的菜单结构）。</summary>
    public static string TrayLabel(HotkeyAction action) => action switch
    {
        HotkeyAction.Bar => "打开窄条",
        HotkeyAction.QuickBar => "快速粘贴",
        HotkeyAction.ClipboardTranslate => "翻译剪贴板",
        HotkeyAction.Library => "管理历史…",
        _ => HotkeyPlan.ActionNames[action],
    };

    /// <summary>一个全局动作现在的组合键；空串或未设给 null。</summary>
    public static string? Combination(HotkeyAction action, AppSettings settings) => action switch
    {
        HotkeyAction.CaptureSelection => NonEmpty(settings.CaptureHotkey),
        HotkeyAction.QuickBar => NonEmpty(settings.QuickBarHotkey),
        HotkeyAction.Bar => NonEmpty(settings.BarHotkey),
        HotkeyAction.ClipboardTranslate => NonEmpty(settings.ClipboardTranslateHotkey),
        HotkeyAction.Library => NonEmpty(settings.LibraryHotkey),
        _ => null,
    };

    private static string? NonEmpty(string text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// Win32 托盘菜单的一行：标签 + 制表符 + 加速键（菜单惯例里 \t 之后的
    /// 文本右对齐成列）。未设键的行只有标签——空列比没列诚实。
    /// </summary>
    public static string TrayMenuLine(HotkeyAction action, AppSettings settings)
        => Combination(action, settings) is { Length: > 0 } key
            ? $"{TrayLabel(action)}\t{key}"
            : TrayLabel(action);

    /// <summary>
    /// 悬停托盘按钮与 ⋯ 菜单的字母（键帽、tooltip、加速键列三处共用）。
    /// 没有字母的动作（粘贴走 Enter、纯文本/定位无键）给 null。
    /// </summary>
    public static string? TrayKey(string actionId) => actionId switch
    {
        "copy" or "open" or "pin" or "favorite" or "note" or "group" or "delete"
            => Find(actionId)?.Bar,
        _ => null,
    };

    /// <summary>按住 Ctrl 时键帽上的短形文本；没有该行给 null。</summary>
    public static string? BadgeText(string rowId)
        => Find(rowId) is { } row ? row.Badge ?? row.Bar : null;

    /// <summary>组合键拆成键帽序列："Ctrl+Shift+B" → Ctrl、Shift、B；未设给空表。</summary>
    public static string[] Chips(string? combination)
        => string.IsNullOrWhiteSpace(combination)
            ? []
            : combination.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>速查区一行的文本：动作 · 各窗按键 ·（条件）。渲染与单测共用一个格式。</summary>
    public static string CheatSheetLine(KeyMapRow row)
    {
        var surfaces = new List<string>();
        if (row.Bar is { Length: > 0 })
        {
            surfaces.Add($"窄条 {row.Bar}");
        }

        if (row.Settings is { Length: > 0 })
        {
            surfaces.Add($"设置 {row.Settings}");
        }

        var line = $"{row.Name}：{string.Join(" · ", surfaces)}";
        return row.Condition is { Length: > 0 } ? $"{line}（{row.Condition}）" : line;
    }

    /// <summary>The whole cheatsheet as lines, in table order.</summary>
    public static IReadOnlyList<string> CheatSheetLines()
        => Rows.Select(CheatSheetLine).ToList();

    /// <summary>One row by action id, or null for an unknown id.</summary>
    public static KeyMapRow? Find(string id)
        => Rows.FirstOrDefault(row => row.Id == id);

    /// <summary>
    /// 辅助技术念出的控件名（票 27 / U-29）：动作名带键，"复制（C）"。
    /// 键取该窗那一列（管理窗与窄条不同键的行由 Library 列覆盖，同键行回落
    /// Bar）；没有键的行只有动作名。名称单源：凡挂在图标钮上的
    /// <c>AutomationProperties.Name</c> 一律从这里取，界面不另写一份——
    /// 尤其是管理窗命令栏：按住 Ctrl 换键帽时可见文字消失，名字是键帽教学
    /// 期间屏幕阅读器唯一的真相。
    /// </summary>
    public static string AutomationName(string id, KeySurface surface = KeySurface.Bar)
    {
        if (Find(id) is not { } row)
        {
            return id;
        }

        var key = surface switch
        {
            KeySurface.Settings => row.Settings,
            KeySurface.Library => row.Library ?? row.Bar,
            _ => row.Bar,
        };

        return key is { Length: > 0 } ? $"{row.Name}（{key}）" : row.Name;
    }
}
