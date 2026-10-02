namespace Shiyu.Core;

/// <summary>
/// 管理窗列表的日期分组（票 24 / UI 报告 §6.4）：今天 / 昨天 / 本周 / 更早。
/// 日期按本地日历日切（与 <see cref="RelativeTime"/> 的"今天/昨天"同一口径：
/// 凌晨复制的东西在早上八点仍属于"今天"）；"本周"是 7 天内的口语周，不是
/// 周一起算的历法周——用户翻历史时想的是"这几天"，不是本周几。
/// </summary>
public static class HistoryGroups
{
    public const string Pinned = "置顶";

    public const string Today = "今天";

    public const string Yesterday = "昨天";

    public const string ThisWeek = "本周";

    public const string Earlier = "更早";

    /// <summary>
    /// The date bucket a timestamp belongs to. Both instants are UTC; the
    /// calendar-day comparison runs in <paramref name="zone"/> (defaults to
    /// local, like the list's relative times, so the two never disagree).
    /// </summary>
    public static string DateKeyOf(DateTimeOffset utc, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;

        if (local.Date == today)
        {
            return Today;
        }

        if (local.Date == today.AddDays(-1))
        {
            return Yesterday;
        }

        if (now - utc < TimeSpan.FromDays(7))
        {
            return ThisWeek;
        }

        return Earlier;
    }

    /// <summary>组序：置顶在最前（页游标先排 pinned），其余按新到旧；未知键殿后。</summary>
    public static int Rank(string key) => key switch
    {
        Pinned => 0,
        Today => 1,
        Yesterday => 2,
        ThisWeek => 3,
        Earlier => 4,
        _ => 5,
    };
}
