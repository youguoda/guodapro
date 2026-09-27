using System.Text;

namespace Shiyu.Core;

/// <summary>逐句对照里的一对：一句原文与配给它的那部分译文。</summary>
public sealed record SentencePair(string Original, string Translated);

/// <summary>
/// 逐句对照：把原文与译文在本地对齐的纯函数。
///
/// 刻意不让 LLM 输出对齐结构——那意味着一次非流式往返与一种只有部分
/// 后端遵守的输出格式；本地切分配对对任何后端成立，也接得住还在流式
/// 半途的译文。切分按句末标点（换行也是句界），数量相等一一配对，不齐
/// 时把多的一侧按比例并拢（Glossy merge_meanings 的思路）：逐句对照是
/// 阅读辅助，不是翻译研究工具，比例近似足够诚实。
/// </summary>
public static class SentenceAlign
{
    public static IReadOnlyList<SentencePair> Pair(string original, string translated)
    {
        var left = Split(original);
        var right = Split(translated);

        if (left.Count == 0 || right.Count == 0)
        {
            return [];
        }

        // 数量向少的一侧看齐：多的一侧被按比例切成同等份数。
        var count = Math.Min(left.Count, right.Count);
        var leftChunks = Partition(left, count);
        var rightChunks = Partition(right, count);

        return leftChunks.Zip(rightChunks, (o, t) => new SentencePair(o, t)).ToList();
    }

    /// <summary>
    /// 把 n 段并成 count 份：第 i 份覆盖原下标 [i·n/count, (i+1)·n/count)。
    /// 整数算术让边界确定可测，n == count 时原样返回。
    /// </summary>
    private static IReadOnlyList<string> Partition(IReadOnlyList<string> segments, int count)
    {
        if (segments.Count == count)
        {
            return segments;
        }

        var chunks = new string[count];
        for (var i = 0; i < count; i++)
        {
            var start = (int)((long)i * segments.Count / count);
            var end = (int)((long)(i + 1) * segments.Count / count);
            chunks[i] = Join(segments.Skip(start).Take(end - start).ToList());
        }

        return chunks;
    }

    /// <summary>并拢片段：前一片以 CJK 字符收尾就不补空格（全角标点自足了）。</summary>
    private static string Join(IReadOnlyList<string> parts)
    {
        if (parts.Count == 1)
        {
            return parts[0];
        }

        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (builder.Length > 0 && !EndsWithCjk(builder[^1]))
            {
                builder.Append(' ');
            }

            builder.Append(part);
        }

        return builder.ToString();
    }

    private static bool EndsWithCjk(char last) => last switch
    {
        >= '\u3040' and <= '\u30FF' => true, // 假名
        >= '\u3400' and <= '\u4DBF' => true, // 汉字扩展 A
        >= '\u4E00' and <= '\u9FFF' => true, // 汉字
        >= '\uAC00' and <= '\uD7A3' => true, // 谚文
        >= '\u3000' and <= '\u303F' => true, // CJK 标点（。！」等）
        >= '\uFF00' and <= '\uFFEF' => true, // 全角形式
        _ => false,
    };

    /// <summary>按句末标点与换行切分；末尾无标点的剩余文本自成一段。</summary>
    private static IReadOnlyList<string> Split(string text)
    {
        var runes = text.EnumerateRunes().ToArray();
        var segments = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < runes.Length; i++)
        {
            var rune = runes[i];

            if (rune.Value is '\n' or '\r')
            {
                Close();
                continue;
            }

            if (IsEnder(rune) && !IsDecimalPoint(runes, i))
            {
                current.Append(rune.ToString());

                // "?!"、"……" 是一句的收尾；句末引号/括号跟着句子走
                // （英文排版句号在引号之内收句，「你好。」同理）。
                while (i + 1 < runes.Length
                       && (IsEnder(runes[i + 1]) || IsCloser(runes[i + 1])))
                {
                    current.Append(runes[++i].ToString());
                }

                Close();
                continue;
            }

            current.Append(rune.ToString());
        }

        Close();
        return segments;

        void Close()
        {
            if (current.Length == 0)
            {
                return;
            }

            var segment = current.ToString().Trim();
            current.Clear();

            if (segment.Length > 0)
            {
                segments.Add(segment);
            }
        }
    }

    /// <summary>句末标点：拉丁三件套加全角对应与省略号。</summary>
    private static bool IsEnder(Rune rune) => rune.Value switch
    {
        '.' => true,
        '!' => true,
        '?' => true,
        '。' => true,
        '！' => true,
        '？' => true,
        '…' => true,
        _ => false,
    };

    /// <summary>收尾引号与括号：被句末标点带着一起归入该句。</summary>
    private static bool IsCloser(Rune rune) => rune.Value switch
    {
        '"' or '\'' or '”' or '’' => true,
        '」' or '』' or '》' or '〉' => true,
        '）' or ')' or ']' or '】' => true,
        _ => false,
    };

    /// <summary>两个数字之间的句点是小数点，不是句界（“3.14”）。</summary>
    private static bool IsDecimalPoint(Rune[] runes, int i)
        => runes[i].Value == '.'
           && i > 0 && Rune.IsDigit(runes[i - 1])
           && i + 1 < runes.Length && Rune.IsDigit(runes[i + 1]);
}
