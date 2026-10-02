using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// Shared behaviour for the self-drawn titlebars of the borderless windows
/// (ticket 33): drag on anything that is not a caption button, double-click
/// to maximize/restore, and the maximized visuals — no shadow margin, no
/// corner rounding, and the maximize glyph becomes the restore glyph.
/// </summary>
internal static class TitlebarChrome
{
    public static void DragOrMaximize(Window window, MouseButtonEventArgs e)
    {
        // Caption buttons must not start a drag: DragMove's modal loop would
        // swallow their click (the same nesting trap as ticket 12's freeze).
        if (IsInsideCaptionButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize(window);
            return;
        }

        if (window.WindowState == WindowState.Normal)
        {
            try
            {
                window.DragMove();
            }
            catch (InvalidOperationException)
            {
                // The mouse was released before DragMove began — harmless.
            }
        }
    }

    public static void ToggleMaximize(Window window)
        => window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>Maximized: no shadow margin, no rounding, restore glyph, and
    /// the window is fitted exactly to the work area — WPF's own maximize on
    /// a borderless window overshoots each edge by the resize border (~7px),
    /// which the ticket 33 probe caught as a grey fringe past the screen.</summary>
    public static void UpdateMaximizeVisuals(Window window, Border shell, Button maximizeButton)
    {
        var maximized = window.WindowState == WindowState.Maximized;
        shell.Margin = maximized ? new Thickness(0) : new Thickness(10);
        shell.CornerRadius = maximized
            // token-ok: 最大化时外壳必须去圆角（贴满工作区的功能值 0，非视觉档）。
            ? new CornerRadius(0)
            : (CornerRadius)Application.Current.FindResource("Radius.Window");
        maximizeButton.Content = maximized ? "\uE923" : "\uE922";

        if (maximized)
        {
            var center = new System.Windows.Point(
                window.Left + window.ActualWidth / 2, window.Top + window.ActualHeight / 2);
            var screenPoint = new Shiyu.Core.ScreenPoint(
                (int)(center.X * GetScaleAt(center).ScaleX),
                (int)(center.Y * GetScaleAt(center).ScaleY));
            var work = ScreenGeometry.WorkAreaAt(screenPoint);
            MoveWindow(
                new System.Windows.Interop.WindowInteropHelper(window).Handle,
                work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top, true);
        }
    }

    private static (double ScaleX, double ScaleY) GetScaleAt(System.Windows.Point diuPoint)
        => ScreenGeometry.ScaleAt(new Shiyu.Core.ScreenPoint((int)diuPoint.X, (int)diuPoint.Y));

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);

    private static bool IsInsideCaptionButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button)
            {
                return true;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }
}
