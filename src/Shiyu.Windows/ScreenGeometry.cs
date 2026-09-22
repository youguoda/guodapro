using Shiyu.Core;

namespace Shiyu.Windows;

/// <summary>Reads where the cursor is and what screen it is on, in real pixels.</summary>
public static class ScreenGeometry
{
    public static ScreenPoint CursorPosition()
        => NativeMethods.GetCursorPos(out var point)
            ? new ScreenPoint(point.X, point.Y)
            : new ScreenPoint(0, 0);

    /// <summary>
    /// The usable area of the screen the point is on — the work area, not the
    /// full monitor, so a badge never hides behind the taskbar.
    /// </summary>
    public static ScreenRect WorkAreaAt(ScreenPoint point)
    {
        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.Point { X = point.X, Y = point.Y },
            NativeMethods.MonitorDefaultToNearest);

        var info = new NativeMethods.MonitorInfo
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };

        if (!NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            // A guess is better than placing the window at the origin of a
            // screen that may not be the one the user is looking at.
            return new ScreenRect(point.X - 400, point.Y - 300, point.X + 400, point.Y + 300);
        }

        return new ScreenRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
    }
}
