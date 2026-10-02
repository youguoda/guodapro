using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// 开机自启的对账（O-40 拆自 App.xaml.cs）：让 Windows 与设置一致。
/// </summary>
internal static class StartupPreference
{
    /// <summary>
    /// Makes Windows agree with the setting. A tray tool nobody starts is a tray
    /// tool nobody has, so this is on by default — but only ever written when
    /// it actually differs, so a user who turned it off is not fought with on
    /// every launch.
    ///
    /// Release builds only: autostart belongs to the installed copy, never to
    /// whatever a development build last left in bin. An entry that points at
    /// another location is taken over, so moving the install moves autostart.
    /// </summary>
    public static void Apply(AppSettings settings)
    {
#if !DEBUG
        var path = Environment.ProcessPath ?? string.Empty;
        if (StartupRegistration.IsEnabled() == settings.StartWithWindows
            && (!settings.StartWithWindows || StartupRegistration.PointsAt(path)))
        {
            return;
        }

        StartupRegistration.Set(settings.StartWithWindows, path);
#endif
    }
}
