namespace Shiyu.Core;

/// <summary>What one of Shiyu's global hotkeys makes happen.</summary>
public enum HotkeyAction
{
    /// <summary>划词翻译：抓前台选中文字并翻译。</summary>
    CaptureSelection,

    /// <summary>唤出快速条。</summary>
    QuickBar,

    /// <summary>唤出/收起常驻窄条。</summary>
    Bar,

    /// <summary>翻译剪贴板内容。</summary>
    ClipboardTranslate,
}

/// <summary>One hotkey that parsed and survived deduplication, ready to register.</summary>
public sealed record HotkeyBinding(HotkeyAction Action, HotkeySpec Spec);

/// <summary>
/// 把设置里的四个热键字符串变成一份注册方案（O-27 下沉候选 3）。
///
/// 这曾是三份各自为政的实现：设置窗校验四键互异、引导只校验三个（漏了
/// 快速条——用户把窄条设成 Ctrl+Shift+V 时，保存照常通过，随后 App 注册
/// 失败，托盘误报"已被其他软件占用"，而占住它的是拾语自己的快速条）、
/// App 注册循环各写各的解析。现在三处都问这一份：
///
/// - 解析四键（<see cref="HotkeySpec.Parse"/>，裸键缺修饰键在此就被拒绝）；
/// - 键间互撞逐对点名——问题文案说清是哪两个动作撞了哪一个组合；
/// - 能注册的照常返回，坏一个不赔上其余三个。
/// </summary>
public static class HotkeyPlan
{
    /// <summary>动作的中文名，注册失败与冲突文案都用它指名道姓。</summary>
    public static readonly IReadOnlyDictionary<HotkeyAction, string> ActionNames =
        new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.CaptureSelection] = "划词翻译",
            [HotkeyAction.QuickBar] = "快速条",
            [HotkeyAction.Bar] = "窄条",
            [HotkeyAction.ClipboardTranslate] = "翻译剪贴板",
        };

    /// <summary>
    /// Builds the registration plan for the four hotkeys in the settings.
    /// Bindings come back in the order Shiyu registers them; Problems are
    /// plain Chinese sentences, each naming the action (or the two actions)
    /// it is about.
    /// </summary>
    public static (IReadOnlyList<HotkeyBinding> Bindings, IReadOnlyList<string> Problems) Build(
        AppSettings settings)
    {
        // Fixed order — the registration order the App has always used, so a
        // collision still resolves the same way it did (first one wins).
        var slots = new (HotkeyAction Action, string? Text)[]
        {
            (HotkeyAction.CaptureSelection, settings.CaptureHotkey),
            (HotkeyAction.QuickBar, settings.QuickBarHotkey),
            (HotkeyAction.Bar, settings.BarHotkey),
            (HotkeyAction.ClipboardTranslate, settings.ClipboardTranslateHotkey),
        };

        var problems = new List<string>();
        var parsed = new List<(HotkeyAction Action, HotkeySpec Spec)>();

        foreach (var (action, text) in slots)
        {
            var spec = HotkeySpec.Parse(text);
            if (spec is null)
            {
                // Parse rejects bare keys (no modifier) as well as unreadable
                // text, so "缺修饰键" is covered here rather than as a second
                // rule that could drift from the first.
                problems.Add(
                    $"{ActionNames[action]}的快捷键「{text?.Trim() ?? ""}」无法识别，"
                    + "需要形如 Ctrl+Shift+Z 且至少带一个修饰键。");
                continue;
            }

            parsed.Add((action, spec));
        }

        // Every colliding pair is named, both sides: "第二个永远不会生效" is
        // only actionable when the user is told which two to separate.
        for (var i = 0; i < parsed.Count; i++)
        {
            for (var j = i + 1; j < parsed.Count; j++)
            {
                if (parsed[i].Spec == parsed[j].Spec)
                {
                    problems.Add(
                        $"{ActionNames[parsed[i].Action]}与{ActionNames[parsed[j].Action]}"
                        + $"的快捷键都是「{parsed[i].Spec}」，后注册的那个永远不会生效——请改掉其中一个。");
                }
            }
        }

        // First registered wins, as RegisterHotKey always resolved it — but
        // here the loser is left out rather than registered to fail, so the
        // collision is reported once, by name, instead of a second time as a
        // misleading "taken by other software".
        var bindings = new List<HotkeyBinding>();
        var seen = new HashSet<HotkeySpec>();
        foreach (var (action, spec) in parsed)
        {
            if (seen.Add(spec))
            {
                bindings.Add(new HotkeyBinding(action, spec));
            }
        }

        return (bindings, problems);
    }
}
