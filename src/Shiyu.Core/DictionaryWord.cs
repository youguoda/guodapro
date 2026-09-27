using System.Text;

namespace Shiyu.Core;

/// <summary>
/// 单词判定：一段选中文本算不算“查词”的对象。
///
/// 词典卡只对“一个词”有意义——选中一个句子还出词典卡是打扰。判定是
/// 纯词形规则（配合 <see cref="LanguageGuess"/> 的文字系统先验），零请求、
/// 零延迟，面板据此决定要不要发起两阶段词典补全。
/// </summary>
public static class DictionaryWord
{
    /// <summary>中文词的长度上限：词与“选多了的无标点短语”之间的边界。</summary>
    private const int MaxChineseRunes = 8;

    public static bool IsEnglishWord(string text)
    {
        var word = text.Trim();
        if (word.Length < 2 || ContainsWhitespace(word))
        {
            // 单个字母（"a"、"I"）更可能是误选的一个字符，不值得一张卡。
            return false;
        }

        if (LanguageGuess.FromText(word).Script != TextScript.Latin)
        {
            return false;
        }

        var runes = word.EnumerateRunes().ToArray();
        for (var i = 0; i < runes.Length; i++)
        {
            if (Rune.IsLetter(runes[i]))
            {
                continue;
            }

            // 连字符与撇号夹在字母之间才算词的一部分（don't / well-known）；
            // 出现在首尾（-hello / hello'）就当它是没选干净的碎片。
            if (runes[i].Value is (int)'-' or (int)'\'' or 0x2019
                && i > 0 && i < runes.Length - 1
                && Rune.IsLetter(runes[i - 1]) && Rune.IsLetter(runes[i + 1]))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    public static bool IsChineseWord(string text)
    {
        var word = text.Trim();
        var runes = word.EnumerateRunes().ToArray();

        if (runes.Length is < 1 or > MaxChineseRunes)
        {
            return false;
        }

        // 全部是汉字才算词：混进假名/谚文/字母/数字/标点都不算。只认
        // 文字系统还不够——「電話」这类纯汉字日文词本地无法与中文区分，
        // 是这套先验固有的取舍，不是断言。
        return runes.All(IsHan);
    }

    /// <summary>送进词典端口的键：去掉首尾空白、拉丁折成小写。</summary>
    public static string LookupKey(string text) => text.Trim().ToLowerInvariant();

    private static bool ContainsWhitespace(string word)
    {
        foreach (var rune in word.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHan(Rune rune) => rune.Value switch
    {
        >= 0x3400 and <= 0x4DBF => true,
        >= 0x4E00 and <= 0x9FFF => true,
        >= 0xF900 and <= 0xFAFF => true,
        _ => false,
    };
}
