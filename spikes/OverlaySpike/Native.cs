using System.Runtime.InteropServices;

namespace OverlaySpike;

/// <summary>
/// The Win32 calls the spike needs to answer its question objectively rather
/// than by eye.
/// </summary>
internal static class Native
{
    internal const int GwlExStyle = -20;
    internal const uint WsExNoActivate = 0x08000000;
    internal const uint WsExToolWindow = 0x00000080;
    internal const uint WsExTransparent = 0x00000020;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// The authoritative answer to "which window would receive a click here".
    /// It applies the same hit-testing the mouse does, including a layered
    /// window's per-pixel alpha — which is exactly what this spike is testing.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassNameW(IntPtr hWnd, [Out] char[] buffer, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] buffer, int maxCount);

    internal static string Describe(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return "(none)";
        }

        var title = new char[256];
        var titleLength = GetWindowTextW(handle, title, title.Length);
        var className = new char[256];
        var classLength = GetClassNameW(handle, className, className.Length);

        GetWindowThreadProcessId(handle, out var processId);

        var name = titleLength > 0 ? new string(title, 0, titleLength) : "(untitled)";
        var cls = classLength > 0 ? new string(className, 0, classLength) : "(unknown)";

        return $"0x{handle:X}  pid={processId}  class={cls}  title={name}";
    }

    internal static void MakeNonActivating(IntPtr handle)
    {
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
    }

    internal static bool HasTransparentStyle(IntPtr handle)
        => (GetWindowLongPtr(handle, GwlExStyle).ToInt64() & WsExTransparent) != 0;
    internal const int SmXVirtualScreen = 76;
    internal const int SmYVirtualScreen = 77;
    internal const int SmCxVirtualScreen = 78;
    internal const int SmCyVirtualScreen = 79;

    internal static readonly IntPtr HwndTopmost = new(-1);
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

}
