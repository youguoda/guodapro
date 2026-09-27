namespace Shiyu.Core;

/// <summary>
/// 文字系统——本地码位统计能可靠分辨的最大粒度。
///
/// 它不装作知道"英文还是法文"：拉丁字母内部无法靠码位区分。
/// 消费方（方向标签、回声换向）要的恰恰是"这套字"级别的先验。
/// </summary>
public enum TextScript
{
    Unknown,

    /// <summary>拉丁字母。先验视角下当作英文看待。</summary>
    Latin,

    /// <summary>汉字（无假名混入时视为中文）。</summary>
    Han,

    /// <summary>出现假名即为日文，无论汉字占比。</summary>
    Japanese,

    /// <summary>谚文。</summary>
    Korean,

    /// <summary>西里尔字母。先验视角下当作俄文看待。</summary>
    Cyrillic,
}

/// <summary>
/// 本地语种先验：按码位统计猜原文写在哪套文字系统里。
///
/// 存在的理由是"不为拿 sourceLang 破坏流式"——让模型先报告源语言
/// 意味着一次额外的非流式往返，而这里只需要在流开始前扫一遍字符，
/// 供方向标签展示与回声换向决策使用，零延迟、零请求。
/// </summary>
public sealed record LanguageGuess(TextScript Script)
{
    public static LanguageGuess FromText(string text) => new(Classify(text));

    /// <summary>面板方向标签用的中文短名；分辨不出时为 null。</summary>
    public string? Label => Script switch
    {
        TextScript.Han => "中文",
        TextScript.Japanese => "日文",
        TextScript.Korean => "韩文",
        TextScript.Cyrillic => "俄文",
        TextScript.Latin => "英文",
        _ => null,
    };

    /// <summary>
    /// 可以直接作为 <see cref="TranslationRequest.TargetLanguage"/> 送进
    /// prompt 的英文名；分辨不出时为 null。拉丁当作 English、西里尔当作
    /// Russian 是先验的取舍，不是断言。
    /// </summary>
    public string? LanguageName => Script switch
    {
        TextScript.Han => "Chinese",
        TextScript.Japanese => "Japanese",
        TextScript.Korean => "Korean",
        TextScript.Cyrillic => "Russian",
        TextScript.Latin => "English",
        _ => null,
    };

    private static TextScript Classify(string text)
    {
        var kana = 0;
        var hangul = 0;
        var han = 0;
        var cyrillic = 0;
        var latin = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is >= 0x3040 and <= 0x30FF
                or >= 0x31F0 and <= 0x31FF
                or >= 0xFF66 and <= 0xFF9F)
            {
                kana++;
            }
            else if (value is >= 0xAC00 and <= 0xD7A3
                or >= 0x1100 and <= 0x11FF
                or >= 0x3130 and <= 0x318F)
            {
                hangul++;
            }
            else if (value is >= 0x3400 and <= 0x4DBF
                or >= 0x4E00 and <= 0x9FFF
                or >= 0xF900 and <= 0xFAFF)
            {
                han++;
            }
            else if (value is >= 0x0400 and <= 0x04FF)
            {
                cyrillic++;
            }
            else if (value is >= 0x41 and <= 0x5A
                or >= 0x61 and <= 0x7A
                or >= 0xC0 and <= 0x24F
                or >= 0x1E00 and <= 0x1EFF)
            {
                latin++;
            }
        }

        // 假名与谚文是专属标记：只要出现，归属就没有悬念。
        if (kana > 0)
        {
            return TextScript.Japanese;
        }

        if (hangul > 0)
        {
            return TextScript.Korean;
        }

        // 汉字按双倍权重参与多数裁决——一个汉字承载的信息约等于两个
        // 字母，"今天下午的 meeting 改期了"应当判中文而非被英文单词
        // 的字母数压过去。拉丁与西里尔平手时偏向西里尔：混排文本里
        // 少数派字母更可能是引用的单词而非正文。
        if (han > 0 && han * 2 >= latin && han * 2 >= cyrillic)
        {
            return TextScript.Han;
        }

        if (cyrillic > 0 && cyrillic >= latin)
        {
            return TextScript.Cyrillic;
        }

        return latin > 0 ? TextScript.Latin : TextScript.Unknown;
    }
}
