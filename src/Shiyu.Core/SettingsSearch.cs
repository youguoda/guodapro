namespace Shiyu.Core;

/// <summary>One search hit: the item and where it lives, so results can say it.</summary>
public sealed record SettingsHit(
    SettingsItem Item,
    string PageTitle,
    string SectionTitle,
    string PageId);

/// <summary>
/// Search over the settings tree. Matching runs against labels, hints, the
/// handwritten keywords, and the page and section titles — the keywords are
/// the point: settings speak product ("图片保留") while users search their own
/// words ("多久删"), and only a hand-written bridge covers that distance.
///
/// Every whitespace-separated token must match somewhere (an AND over words),
/// so "删除 保护" narrows rather than floods. Results are capped: a search
/// that fills the screen stops being a search.
/// </summary>
public static class SettingsSearch
{
    public const int ResultCap = 12;

    public static IReadOnlyList<SettingsHit> Find(string query)
    {
        var tokens = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .ToList();

        if (tokens.Count == 0)
        {
            return [];
        }

        var hits = new List<SettingsHit>();

        foreach (var page in SettingsSchema.Tree)
        {
            foreach (var section in page.Sections)
            {
                foreach (var item in section.Items)
                {
                    if (tokens.All(token => Matches(token, item, page, section)))
                    {
                        hits.Add(new SettingsHit(item, page.Title, section.Title, page.Id));
                        if (hits.Count >= ResultCap)
                        {
                            return hits;
                        }
                    }
                }
            }
        }

        return hits;
    }

    private static bool Matches(string token, SettingsItem item, SettingsPage page, SettingsSection section)
        => item.Label.ToLowerInvariant().Contains(token)
            || (item.Hint?.ToLowerInvariant().Contains(token) ?? false)
            || item.KeywordList.Any(keyword => keyword.ToLowerInvariant().Contains(token))
            || page.Title.ToLowerInvariant().Contains(token)
            || section.Title.ToLowerInvariant().Contains(token);
}
