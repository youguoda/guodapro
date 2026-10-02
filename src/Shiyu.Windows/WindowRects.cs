using Shiyu.Core;

namespace Shiyu.Windows;

/// <summary>
/// A window's placement facts, in physical pixels, read and written straight
/// through the platform layer (O-39) — the one coordinate source that is
/// right on every monitor and scale combination, because it never passes
/// through WPF's per-monitor transforms.
/// </summary>
public static class WindowRects
{
    /// <summary>
    /// The window's current rectangle. False (and a zero rect) when the
    /// handle is gone — a window mid-teardown answers nothing.
    /// </summary>
    public static bool TryGet(IntPtr handle, out ScreenRect rect)
    {
        if (handle != IntPtr.Zero && NativeMethods.GetWindowRect(handle, out var native))
        {
            rect = new ScreenRect(native.Left, native.Top, native.Right, native.Bottom);
            return true;
        }

        rect = default;
        return false;
    }

    /// <summary>
    /// Moves and resizes in physical pixels, repainting afterwards — the
    /// maximized borderless window is fitted to the work area exactly this
    /// way, because WPF's own maximize overshoots each edge by the resize
    /// border (ticket 33).
    /// </summary>
    public static void Move(IntPtr handle, int x, int y, int width, int height)
        => NativeMethods.MoveWindow(handle, x, y, width, height, repaint: true);
}
