namespace Shiyu.Core;

public enum SettingsControl
{
    /// <summary>Exclusive choice shown as one row of joined buttons.</summary>
    Segmented,

    Toggle,
    Number,
    Text,

    /// <summary>A secret: never echoed back, blank means keep the stored one.</summary>
    Password,

    /// <summary>A hotkey combination, written like Ctrl+Shift+Z.</summary>
    Hotkey,

    /// <summary>An ordered list of named actions, comma separated.</summary>
    Actions,

    /// <summary>Free-form text across lines.</summary>
    Multiline,

    /// <summary>A folder path, with a browse affordance.</summary>
    Directory,

    /// <summary>Shown, not edited.</summary>
    ReadOnly,

    /// <summary>A row the window builds itself (e.g. the backup buttons).</summary>
    Custom,
}

/// <summary>
/// One setting as data: what it is, how it is edited, where its words live.
/// The interface renders the tree and nothing else — adding a setting adds a
/// leaf here plus a value binding, and the renderer never learns about it.
/// </summary>
public sealed record SettingsItem(
    string Id,
    string Label,
    SettingsControl Control,
    string? Hint = null,
    double Min = 0,
    double Max = 0,
    string[]? Choices = null,
    string? Parent = null,
    string[]? Keywords = null)
{
    public string[] ChoiceList => Choices ?? [];
    public string[] KeywordList => Keywords ?? [];
}

public sealed record SettingsSection(string Id, string Title, params SettingsItem[] Items);

public sealed record SettingsPage(string Id, string Title, params SettingsSection[] Sections);

public static class SettingsSchema
{
    /// <summary>
    /// The whole settings surface, organised by what the user came to do —
    /// record, act, look, press, serve, store — never by code module, so the
    /// page names are directions rather than implementation.
    /// </summary>
    public static readonly IReadOnlyList<SettingsPage> Tree =
    [
        new SettingsPage("record", "记录",
            new SettingsSection("record.kinds", "记录什么",
                new SettingsItem(
                    "record.images", "记录图片", SettingsControl.Toggle,
                    Hint: "复制图片内容时是否入库。",
                    Keywords: ["图片", "截图", "记录"]),
                new SettingsItem(
                    "record.files", "记录文件", SettingsControl.Toggle,
                    Hint: "复制文件时是否入库（文件列表与图片文件预览）。",
                    Keywords: ["文件", "记录"])),
            new SettingsSection("record.exclusions", "不记录",
                new SettingsItem(
                    "exclusions", "排除规则", SettingsControl.Multiline,
                    Hint: "一行一条：应用名精确匹配，或 re: 开头的正则匹配内容。常见密码管理器始终排除，无需重复填写。",
                    Keywords: ["排除", "隐私", "密码", "不记录", "敏感"]))),

        new SettingsPage("actions", "动作",
            new SettingsSection("actions.hover", "悬停动作",
                new SettingsItem(
                    "bar.actions", "动作清单", SettingsControl.Actions,
                    Hint: "逗号分隔，顺序即显示顺序。对某条记录不适用的动作会自动隐藏。",
                    Keywords: ["悬停", "托盘", "按钮", "复制", "粘贴", "删除"])),
            new SettingsSection("actions.feedback", "反馈",
                new SettingsItem(
                    "action.sound", "动作完成后播放提示音", SettingsControl.Toggle,
                    Hint: "默认只显示对勾。",
                    Keywords: ["声音", "提示音", "反馈"]))),

        new SettingsPage("look", "界面",
            new SettingsSection("look.appearance", "外观",
                new SettingsItem(
                    "theme", "主题", SettingsControl.Segmented,
                    Choices: ["跟随系统", "浅色", "深色"],
                    Keywords: ["主题", "深色", "浅色", "夜间", "夜间模式", "变暗"]),
                new SettingsItem(
                    "bar.at-cursor", "光标旁呼出", SettingsControl.Toggle,
                    Hint: "窄条出现在光标/输入位置附近，与系统 Win+V 面板一致；关闭则固定在你上次拖放的位置。",
                    Keywords: ["位置", "光标", "呼出", "弹出", "输入"]),
                new SettingsItem(
                    "look.bar-topmost", "窄条置顶", SettingsControl.Toggle,
                    Hint: "窄条保持在其他窗口之上；关闭后可被其他窗口遮挡，置顶状态由窄条头部的图钉按钮随时切换。",
                    Keywords: ["置顶", "图钉", "压住", "遮挡", "窗口", "最前", "浮在最上层"]),
                new SettingsItem(
                    "look.preview-hover", "悬停预览延迟", SettingsControl.Number, Min: 0, Max: 2000,
                    Hint: "鼠标在卡片上停留多少毫秒后弹出完整预览；0 表示关闭悬停预览（按住空格仍可预览）。",
                    Keywords: ["预览", "悬停", "停留", "空格", "完整", "延迟"])),
            new SettingsSection("look.density", "密度",
                new SettingsItem(
                    "bar.text-lines", "文本行数", SettingsControl.Number, Min: 1, Max: 20,
                    Hint: "窄条里每张文本卡最多显示的行数。",
                    Keywords: ["行数", "密度", "文本"]),
                new SettingsItem(
                    "bar.image-height", "图片高度", SettingsControl.Number, Min: 40, Max: 400,
                    Keywords: ["图片", "高度", "密度"]),
                new SettingsItem(
                    "bar.file-count", "文件条数", SettingsControl.Number, Min: 1, Max: 10,
                    Hint: "文件卡里最多列出的文件行数。",
                    Keywords: ["文件", "条数", "密度"]),
                new SettingsItem(
                    "look.lightweight", "轻量模式", SettingsControl.Toggle,
                    Hint: "窄条隐藏后释放界面资源、压缩常驻内存；期间复制的内容照常记录。",
                    Keywords: ["内存", "占用", "轻量", "后台", "常驻"]))),

        new SettingsPage("hotkeys", "快捷键",
            new SettingsSection("hotkeys.all", "全局",
                new SettingsItem("hotkey.capture", "划词翻译", SettingsControl.Hotkey, Keywords: ["划词", "翻译", "快捷键"]),
                new SettingsItem("hotkey.clipboard", "翻译剪贴板", SettingsControl.Hotkey, Keywords: ["剪贴板", "翻译", "快捷键"]),
                new SettingsItem("hotkey.quickbar", "快速条", SettingsControl.Hotkey, Keywords: ["快速条", "快捷键"]),
                new SettingsItem("hotkey.bar", "窄条", SettingsControl.Hotkey, Keywords: ["窄条", "快捷键"])),
            new SettingsSection("hotkeys.drag", "划词",
                new SettingsItem(
                    "hotkeys.selection-badge", "拖选后出翻译徽标", SettingsControl.Toggle,
                    Hint: "在任意应用里拖选文字后，光标旁浮现「译」徽标，点击才翻译，无需记快捷键。需要常驻全局鼠标监听（低级鼠标钩子，每一次鼠标事件都会多绕一段拾语），默认关闭；关闭即刻摘钩，拾语退出时钩子自动还给系统。与「划词翻译」热键并存，互不影响。",
                    Keywords: ["划词", "拖选", "选中", "徽标", "翻译", "鼠标", "钩子", "监听", "开销"])),
            new SettingsSection("hotkeys.winv", "系统按键",
                new SettingsItem(
                    "winv.takeover", "接管 Win+V", SettingsControl.Toggle,
                    Hint: "让 Win+V 唤起拾语窄条，代替系统剪贴板面板。默认关闭；关闭即刻还原，拾语退出或被强杀时 Win+V 自动回到系统行为。与「窄条」热键并存：两者都开时，Win+V 与该热键都能唤起窄条。其它 Win 组合键不受影响。",
                    Keywords: ["win", "winv", "接管", "系统", "剪贴板", "面板", "热键"]))),

        new SettingsPage("service", "服务",
            new SettingsSection("service.languages", "翻译语言",
                new SettingsItem("service.target-language", "译文语言", SettingsControl.Text, Keywords: ["语言", "翻译", "目标"]),
                new SettingsItem(
                    "service.source-language", "源语言", SettingsControl.Text,
                    Hint: "留空表示自动检测。",
                    Keywords: ["语言", "源", "自动"])),
            new SettingsSection("service.backend", "模型服务",
                new SettingsItem(
                    "service.backend-kind", "翻译方式", SettingsControl.Segmented,
                    Choices: ["公共通道（即将推出）", "自备密钥"],
                    Hint: "自备密钥：使用你自己的 OpenAI 兼容接口与凭据，从下面的服务商预设开始最快。公共通道零配置，但上线条件（自定义域名大陆可达、服务端加固、费用有人承担）尚未满足，暂不可选。",
                    Keywords: ["公共", "免费", "通道", "中继", "翻译", "后端", "密钥", "零配置", "隐私", "即将推出", "自备"]),
                new SettingsItem(
                    "service.preset", "服务商预设", SettingsControl.Custom,
                    Hint: "选中即填好服务地址与模型；手改地址或模型则视为自定义。预设附带「申请密钥」直达与「测试连接」。",
                    Keywords: ["预设", "服务商", "百炼", "阿里云", "deepseek", "智谱", "glm", "硅基流动", "kimi", "测试连接", "申请密钥", "自定义"]),
                new SettingsItem(
                    "service.base-url", "服务地址", SettingsControl.Text,
                    Hint: "自备密钥时使用：OpenAI 兼容接口地址。",
                    Keywords: ["接口", "地址", "服务", "后端"]),
                new SettingsItem(
                    "service.model", "模型", SettingsControl.Text,
                    Hint: "自备密钥时使用。",
                    Keywords: ["模型", "服务"]),
                new SettingsItem(
                    "service.api-key", "凭据", SettingsControl.Password,
                    Hint: "自备密钥时使用。已保存的凭据不回显。留空表示不改动，填入则覆盖。",
                    Keywords: ["凭据", "密钥", "api", "key"]))),

        new SettingsPage("store", "数据",
            new SettingsSection("store.retention", "清理",
                new SettingsItem(
                    "store.retention-days", "图片保留", SettingsControl.Number, Min: 1, Max: 36500,
                    Hint: "天后清理原图；文本永不清理。",
                    Keywords: ["保留", "清理", "图片", "原图", "多久删", "过期", "几天", "占用"]),
                new SettingsItem(
                    "store.protect", "删除保护", SettingsControl.Toggle,
                    Hint: "受收藏/置顶保护的条目不参与自动清理与批量删除，也不显示删除入口；取消标记即可删除。",
                    Keywords: ["保护", "收藏", "置顶", "删除", "误删", "防手滑"]),
                new SettingsItem(
                    "store.protect-favorites", "保护收藏", SettingsControl.Toggle,
                    Parent: "store.protect", Keywords: ["保护", "收藏", "星标"]),
                new SettingsItem(
                    "store.protect-pinned", "保护置顶", SettingsControl.Toggle,
                    Parent: "store.protect", Keywords: ["保护", "置顶", "钉住"])),
            new SettingsSection("store.location", "位置与启动",
                new SettingsItem(
                    "store.directory", "数据位置", SettingsControl.Directory,
                    Hint: "历史与图片存在这里；留空用默认位置。",
                    Keywords: ["位置", "目录", "数据", "移动", "迁移", "换盘"]),
                new SettingsItem(
                    "store.start-with-windows", "开机自启", SettingsControl.Toggle,
                    Keywords: ["开机", "自启", "启动"]),
                new SettingsItem(
                    "store.backup", "备份", SettingsControl.Custom,
                    Hint: "导出全部历史、图片原图与设置；可加密。导入可合并或覆盖。",
                    Keywords: ["备份", "导出", "导入", "加密"]),
                new SettingsItem(
                    "store.usage", "磁盘占用", SettingsControl.Custom,
                    Hint: "数据库、图片原图与合计占用；可一键打开数据所在文件夹。",
                    Keywords: ["占用", "磁盘", "大小", "空间", "多少"]))),

        new SettingsPage("about", "关于",
            new SettingsSection("about.app", "拾语",
                new SettingsItem("about.version", "版本", SettingsControl.ReadOnly, Keywords: ["版本"]),
                new SettingsItem(
                    "about.update-auto", "自动检查更新", SettingsControl.Toggle,
                    Hint: "启动后悄悄查一次 GitHub Releases；发现新版本只提醒，安装永远要你亲手点。手动入口：托盘菜单「检查更新」。",
                    Keywords: ["更新", "升级", "版本", "检查", "github"]),
                new SettingsItem(
                    "about.onboarding", "新手引导", SettingsControl.Custom,
                    Hint: "重新运行首次启动时的引导。",
                    Keywords: ["引导", "首次", "新手"]))),
    ];

    public static SettingsItem? Find(string id)
        => Tree.SelectMany(page => page.Sections)
            .SelectMany(section => section.Items)
            .FirstOrDefault(item => item.Id == id);
}
