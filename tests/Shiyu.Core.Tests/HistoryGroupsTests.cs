using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// 日期分组的边界（票 24 / UI 报告 §6.4）：分组头是列表的导航地图，一跳
/// 错一天，用户就在错误的抽屉里翻东西。日历日切分用固定时区钉住，不受
/// 跑测试的机器时区影响。
/// </summary>
public class HistoryGroupsTests
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("probe", TimeSpan.FromHours(8), "probe", "probe");

    [Fact]
    public void Buckets_follow_calendar_days_not_rolling_hours()
    {
        var now = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(8));

        // 凌晨复制、早上看：还是"今天"（日历日，不是滚动 24 小时）。
        Assert.Equal(HistoryGroups.Today,
            HistoryGroups.DateKeyOf(now.AddHours(-7), now, Zone));
        Assert.Equal(HistoryGroups.Yesterday,
            HistoryGroups.DateKeyOf(now.AddDays(-1).AddHours(1), now, Zone));
        Assert.Equal(HistoryGroups.ThisWeek,
            HistoryGroups.DateKeyOf(now.AddDays(-3), now, Zone));
        Assert.Equal(HistoryGroups.Earlier,
            HistoryGroups.DateKeyOf(now.AddDays(-9), now, Zone));
    }

    [Fact]
    public void Yesterday_takes_priority_over_this_week()
    {
        // 30 小时前既是"7 天内"也是"上一个日历日"——用户心里的分组是后者。
        var now = new DateTimeOffset(2026, 10, 1, 23, 30, 0, TimeSpan.FromHours(8));
        Assert.Equal(HistoryGroups.Yesterday,
            HistoryGroups.DateKeyOf(now.AddHours(-30), now, Zone));
    }

    [Fact]
    public void Rank_orders_pinned_first_then_newest_to_oldest()
    {
        var ordered = new[] { HistoryGroups.Earlier, HistoryGroups.Today, HistoryGroups.Pinned,
            HistoryGroups.Yesterday, HistoryGroups.ThisWeek }
            .OrderBy(HistoryGroups.Rank)
            .ToArray();

        Assert.Equal(
            [HistoryGroups.Pinned, HistoryGroups.Today, HistoryGroups.Yesterday,
             HistoryGroups.ThisWeek, HistoryGroups.Earlier],
            ordered);
    }

    [Fact]
    public void Unknown_keys_sort_last_not_first()
    {
        Assert.True(HistoryGroups.Rank(HistoryGroups.Earlier) < HistoryGroups.Rank("来自未来版本的分组"));
    }
}
