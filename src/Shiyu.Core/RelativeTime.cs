namespace Shiyu.Core;

/// <summary>
/// Timestamps as people read them: a list wants "3 分钟前", not "09-25 22:28".
/// The absolute stamp stays available wherever a tooltip can carry it.
/// </summary>
public static class RelativeTime
{
    /// <summary>
    /// The relative form of <paramref name="utc"/> as of <paramref name="now"/>.
    /// Both are UTC instants; the calendar-day check (昨天) uses
    /// <paramref name="now"/>'s local date, supplied by the caller.
    /// </summary>
    public static string For(DateTimeOffset utc, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var delta = now - utc;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalMinutes < 1)
        {
            return "刚刚";
        }

        if (delta.TotalHours < 1)
        {
            return $"{(int)delta.TotalMinutes} 分钟前";
        }

        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);

        if (local.Date == localNow.Date)
        {
            return $"今天 {local:HH:mm}";
        }

        if (local.Date == localNow.Date.AddDays(-1))
        {
            return $"昨天 {local:HH:mm}";
        }

        if (delta.TotalDays < 7)
        {
            return $"{(int)delta.TotalDays} 天前";
        }

        return local.Year == localNow.Year ? $"{local:MM-dd}" : $"{local:yyyy-MM-dd}";
    }
}
