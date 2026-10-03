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

    /// <summary>
    /// Enters the system's caption-drag loop and returns when it ends (the
    /// button comes back up). Callers tell a click from a drag by comparing
    /// the rectangle before and after: this HTCAPTION route instead of WPF's
    /// DragMove because DragMove swallows the mouse-up, leaving no way to give
    /// one grip both behaviours (user request 2026-10-05: the bar's brand
    /// mark drags the window, a plain click still opens the library).
    /// </summary>
    public static void RunCaptionDrag(IntPtr handle, int screenX, int screenY)
        => NativeMethods.SendMessage(
            handle,
            0x00A1,                       // WM_NCLBUTTONDOWN
            (IntPtr)0x0002,               // HTCAPTION
            (IntPtr)((screenY << 16) | (screenX & 0xFFFF)));
}
