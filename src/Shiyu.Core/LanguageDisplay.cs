namespace Shiyu.Core;

/// <summary>
/// 语言名的中文显示（票 22 / §6.2 头部）：设置里的目标语言值（English 等）
/// 与 <see cref="LanguageGuess"/> 的先验短名（英文/日文…）走同一张映射表，
/// 面板头部不再中英混排。认识不了的名字原样显示——绝不替用户猜。
/// </summary>
public static class LanguageDisplay
{
    public static string Name(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        return trimmed.ToLowerInvariant() switch
        {
            "english" or "英文" or "英语" => "英语",
            "chinese" or "mandarin" or "中文" or "汉语" => "中文",
            "japanese" or "日文" or "日语" => "日语",
            "korean" or "韩文" or "韩语" => "韩语",
            "russian" or "俄文" or "俄语" => "俄语",
            "french" or "法文" or "法语" => "法语",
            "german" or "德文" or "德语" => "德语",
            "spanish" or "西班牙文" or "西班牙语" => "西班牙语",
            _ => trimmed,
        };
    }
}
