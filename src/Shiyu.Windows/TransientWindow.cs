using Shiyu.Core;

namespace Shiyu.Windows;

/// <summary>
/// The window traits the badge and the panel both need: visible, on top, and
/// incapable of taking focus away from whatever the user is actually doing.
///
/// Exists so the application layer never reaches into the interop itself.
/// </summary>
public static class TransientWindow
{
    /// <summary>
    /// Marks a window as one that is never activated — clicking it leaves the
    /// user's caret and selection exactly where they were.
    /// </summary>
    public static void MakeNonActivating(IntPtr handle)
    {
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(
            handle,
            NativeMethods.GwlExStyle,
            new IntPtr(style | NativeMethods.WsExNoActivate | NativeMethods.WsExTopmost));
    }

    /// <summary>
    /// Moves a window in physical screen pixels, sidestepping the scaled
    /// coordinate system entirely — which is what makes it behave on a desk
    /// with monitors at different scale factors.
    /// </summary>
    public static void MoveTo(IntPtr handle, ScreenPoint position)
        => NativeMethods.SetWindowPos(
            handle, NativeMethods.HwndTopmost, position.X, position.Y, 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
}
