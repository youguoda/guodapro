namespace Shiyu.Core;

/// <summary>划词过滤链的裁决——每个值对应链上一环，全链单测钉住（票 37）。</summary>
public enum SelectionBadgeVerdict
{
    /// <summary>通过全链：值得为这段选中文字浮出徽标。</summary>
    Offer,

    /// <summary>总开关关着——链首即止。</summary>
    Disabled,

    /// <summary>前台是桌面/shell——在那里模拟 Ctrl+C 毫无意义。</summary>
    DesktopShell,

    /// <summary>短于最小长度——多半是误触或碎片。</summary>
    TooShort,

    /// <summary>不含任何字母——数字、标点没有可翻译的东西。</summary>
    NoLetters,

    /// <summary>路径形（C:\…、\\…、/usr/…）——是地址，不是文句。</summary>
    PathLike,
}

/// <summary>
/// 划词徽章的过滤链：总开关 → 最小长度 → 须含字母 → 拒绝路径形 → 桌面早退。
///
/// 链在运行时拆成两段：桌面早退必须发生在模拟 Ctrl+C <b>之前</b>（取词是
/// 有副作用的手续——借剪贴板、向别人的窗口发按键——不值得为注定丢弃的
/// 结果做一遍）；文字三环只在取到文字之后才有输入。两段各自按链上顺序
/// 判定，先命中的裁决即最终裁决。
/// </summary>
public static class SelectionBadgeFilter
{
    /// <summary>
    /// 两个字符起。单字符的按下-抬起多半是点击残留或手抖，为它弹徽标是
    /// 打扰；真要查单个字，划词热键永远在。
    /// </summary>
    public const int MinimumLength = 2;

    /// <summary>取词之前的早退判定：总开关 → 桌面早退。</summary>
    public static SelectionBadgeVerdict JudgeBeforeCapture(bool enabled, bool desktopForeground)
    {
        if (!enabled)
        {
            return SelectionBadgeVerdict.Disabled;
        }

        // 桌面上 Ctrl+C 只会复制剪贴板里原有的东西。
        if (desktopForeground)
        {
            return SelectionBadgeVerdict.DesktopShell;
        }

        return SelectionBadgeVerdict.Offer;
    }

    /// <summary>取到文字后的判定：最小长度 → 须含字母 → 拒绝路径形。</summary>
    public static SelectionBadgeVerdict JudgeCapturedText(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.Length < MinimumLength)
        {
            return SelectionBadgeVerdict.TooShort;
        }

        // Unicode 感知：汉字也是字母。在这里用 A-Za-z 会把整句中文判成数字串。
        if (!trimmed.Any(char.IsLetter))
        {
            return SelectionBadgeVerdict.NoLetters;
        }

        if (ContentClassifier.LooksLikeFilePath(trimmed))
        {
            return SelectionBadgeVerdict.PathLike;
        }

        return SelectionBadgeVerdict.Offer;
    }
}
