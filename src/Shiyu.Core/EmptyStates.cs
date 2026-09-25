namespace Shiyu.Core;

/// <summary>What the empty list says, and whether clearing filters can help.</summary>
public sealed record EmptyStateCopy(string Headline, string Hint, bool OfferClear);

/// <summary>
/// The words a bar with nothing in it shows.
///
/// An empty list is the moment the user most needs help and the moment most
/// products say only "无结果" — which reads as breakage. Two empties are
/// fundamentally different and say so: a history with nothing in it sends the
/// user to copy something; a filter that ate everything sends them to loosen
/// a condition. The headline names the exact combination that produced the
/// emptiness, so the way out is obvious without a manual.
/// </summary>
public static class EmptyStates
{
    public static EmptyStateCopy For(HistoryFilter filter, string? groupName, int totalEntries)
    {
        if (totalEntries == 0)
        {
            return new EmptyStateCopy(
                "还没有任何记录",
                "复制一段文字、一张图或几个文件，它们会出现在这里。",
                OfferClear: false);
        }

        var scope = !string.IsNullOrWhiteSpace(groupName) ? $"分组「{groupName}」里" : string.Empty;
        var match = !string.IsNullOrWhiteSpace(filter.Query) ? $"包含「{filter.Query.Trim()}」的" : string.Empty;
        var favourite = filter.Favorite == true ? "收藏的" : string.Empty;
        var kind = filter.Kind switch
        {
            EntryKind.Text => "文本",
            EntryKind.Image => "图片",
            EntryKind.Files => "文件",
            _ => string.Empty,
        };
        var noun = kind.Length == 0 ? "内容" : kind + "内容";

        var headline = $"{scope}还没有{match}{favourite}{noun}";

        var suggestions = new List<string>();
        if (scope.Length > 0)
        {
            suggestions.Add($"切回「全部」、或把条目归进「{groupName!.Trim()}」");
        }
        if (match.Length > 0)
        {
            suggestions.Add("换个说法或清掉关键词");
        }
        if (kind.Length > 0)
        {
            suggestions.Add("类型换成「全部」试试");
        }
        if (favourite.Length > 0)
        {
            suggestions.Add("取消只看收藏");
        }

        if (suggestions.Count == 0)
        {
            // Unfiltered and non-empty cannot show an empty list; if it ever
            // does, the honest word is the generic one.
            suggestions.Add("松开一两个条件，看看全部");
        }

        return new EmptyStateCopy(headline, string.Join("；", suggestions) + "。", OfferClear: true);
    }
}
