using System.Globalization;
using System.Text;

namespace Shiyu.Core;

/// <summary>
/// 回声检测：模型把原文当作译文原样吐回来。
///
/// 这不是假设性失败——prompt 明文允许"已是目标语言就原样返回"，于是
/// 方向判断错误（中文原文配中文目标）时的"正确"行为恰恰是回声。检测
/// 只在流结束后做一次，判定为归一化后的相等或高度相似，刻意保守：
/// 宁可放过一个差劲的译文，也不把正常翻译打成回声触发无谓重试。
/// </summary>
public static class TranslationEcho
{
    /// <summary>
    /// 归一化后字符二元组（bigram）Dice 相似度达到该值即视为回声。
    /// 0.9 只容下标点与大小写级别的差异，换词即越界。
    /// </summary>
    private const double SimilarityThreshold = 0.9;

    public static bool IsEchoish(string original, string translated)
    {
        var left = Normalize(original);
        var right = Normalize(translated);

        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        if (left == right)
        {
            return true;
        }

        // 文字系统不同就不可能是"原文吐回"：本地先验先行，既省掉一次
        // 相似度计算，也挡住跨文字系统偶发的高相似误报。
        if (LanguageGuess.FromText(original).Script != LanguageGuess.FromText(translated).Script)
        {
            return false;
        }

        return Similarity(left, right) >= SimilarityThreshold;
    }

    /// <summary>
    /// 折大小写、去空白、去标点符号：回声往往裹着一层标点或引号的
    /// 差异（正是 <see cref="TranslationCleanup"/> 未必剥得掉的那层）。
    /// </summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.SpaceSeparator
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.OtherPunctuation
                or UnicodeCategory.DashPunctuation
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.MathSymbol
                or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.OtherSymbol)
            {
                continue;
            }

            builder.Append(Rune.ToLowerInvariant(rune));
        }

        return builder.ToString();
    }

    /// <summary>字符二元组的 Dice 相似度，衡量"归一化后还剩多少相同"。</summary>
    private static double Similarity(string left, string right)
    {
        if (left.Length < 2 || right.Length < 2)
        {
            // 单字符没有 bigram；完全相等的情况已在调用方排除，
            // 剩下的只能是不相似。
            return 0;
        }

        var counts = new Dictionary<string, int>();
        for (var i = 0; i < left.Length - 1; i++)
        {
            var gram = left.Substring(i, 2);
            counts[gram] = counts.GetValueOrDefault(gram) + 1;
        }

        var matches = 0;
        for (var i = 0; i < right.Length - 1; i++)
        {
            var gram = right.Substring(i, 2);
            if (counts.GetValueOrDefault(gram) > 0)
            {
                counts[gram]--;
                matches++;
            }
        }

        return 2.0 * matches / ((left.Length - 1) + (right.Length - 1));
    }
}
