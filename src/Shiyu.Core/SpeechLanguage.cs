namespace Shiyu.Core;

/// <summary>
/// 朗读语言：把译文的目标语言名折成 SAPI 音色挑选要的文化前缀。
///
/// 放在 Core 是因为它和 <see cref="LanguageGuess"/>、面板方向标签共享同一
/// 套语言词汇（Chinese/English/Japanese/Korean/Russian）；认识的名字给
/// 两字母前缀，认识不了就 null——调用方拿默认音色，绝不为朗读猜语种。
/// </summary>
public static class SpeechLanguage
{
    public static string? CulturePrefix(string? languageName)
        => languageName?.Trim().ToLowerInvariant() switch
        {
            "chinese" or "mandarin" => "zh",
            "english" => "en",
            "japanese" => "ja",
            "korean" => "ko",
            "russian" => "ru",
            _ => null,
        };
}
