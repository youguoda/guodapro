using Microsoft.Win32;

namespace Shiyu.Windows;

/// <summary>
/// Whether Windows starts Shiyu at sign-in.
///
/// Written to the per-user Run key rather than a scheduled task or a machine-
/// wide entry: it needs no elevation, it is where users look when they wonder
/// what starts with their computer, and it is trivially removable by hand.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Shiyu";

    /// <summary>
    /// Reads what Windows actually does, not what a settings file claims.
    /// The two can disagree — a user may have removed the entry themselves —
    /// and the setting should follow reality rather than argue with it.
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            // expected: 注册表被组策略锁住——按"未开机自启"读，别拦启动。
            return false;
        }
    }

    /// <summary>
    /// Whether the entry launches this very executable. A copy moved or
    /// reinstalled elsewhere leaves an entry that is "enabled" but starts the
    /// old location — or nothing at all.
    /// </summary>
    public static bool PointsAt(string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value
                && string.Equals(value.Trim('"'), executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // expected: 同 IsEnabled——读不了就当不指向本程序。
            return false;
        }
    }

    /// <summary>Returns whether the change stuck.</summary>
    public static bool Set(bool enabled, string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                // Quoted: the path runs through Program Files often enough, and
                // an unquoted path with a space in it is a classic way to have
                // Windows launch something else entirely.
                key.SetValue(ValueName, $"\"{executablePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return IsEnabled() == enabled;
        }
        catch (Exception)
        {
            // expected: 写不进注册表（策略限制）——回报"没写成"，调用方
            // 下次启动照 Windows 现实重试，不拦任何流程。
            return false;
        }
    }
}
