using System.Text;

namespace Shiyu.Windows;

/// <summary>
/// 划词过滤链上的桌面早退闸（票 37）：前台是桌面或任务栏等 shell 表面时，
/// 模拟 Ctrl+C 毫无意义——桌面上 Ctrl+C 只会复制剪贴板里原有的东西。
/// 判定按前台窗口的窗口类，一次字符串查询，不开进程、不查路径。
/// </summary>
public static class DesktopShell
{
    /// <summary>
    /// 桌面与任务栏的窗口类。Progman/WorkerW 是桌面（双击切换壁纸层），
    /// Shell_TrayWnd 系是任务栏——在这些表面上没有"选中文本"可言。
    /// </summary>
    private static readonly string[] ShellWindowClasses =
        ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public static bool IsForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        var name = new StringBuilder(capacity: 64);
        var length = NativeMethods.GetClassNameW(foreground, name, name.Capacity);
        if (length <= 0)
        {
            return false;
        }

        var className = name.ToString(0, length);
        return ShellWindowClasses.Contains(className);
    }
}
