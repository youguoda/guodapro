using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Shiyu.App;

/// <summary>Which system material a window asks DWM to paint behind it.</summary>
internal enum BackdropKind
{
    /// <summary>No material — only rounding and the dark-mode flag apply.</summary>
    None,

    /// <summary>Wallpaper-sampled, for long-lived windows.</summary>
    Mica,

    /// <summary>Real-time blur, for transient surfaces.</summary>
    Acrylic,
}

/// <summary>
/// Applies Windows 11's system backdrops through DWM, per ticket 03's spike:
/// the material renders only on layered windows (a normal WPF window's opaque
/// redirect surface paints black right over it), so callers attach kinds to
/// the layered surfaces and the rest get rounding plus the dark-mode flag.
///
/// Every DWM call is best-effort: a failure — an old system, a future change —
/// leaves the window on its own translucent brush, which is exactly ticket 03's
/// degradation path, and costs the product zero version branches.
/// </summary>
internal static class Backdrop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int BACKDROP_MICA = 2;
    private const int BACKDROP_ACRYLIC = 3;

    private static readonly List<(Window Window, Func<BackdropKind> Kind)> Attached = [];

    /// <summary>Whether the theme currently on is Dark — set by the ThemeManager.</summary>
    public static bool DarkTheme { get; set; }

    /// <summary>
    /// Attaches a material to a window for its whole lifetime and re-applies it
    /// whenever the theme moves. Safe to call from the constructor: the DWM
    /// work waits for the handle to exist.
    /// </summary>
    public static void Attach(Window window, Func<BackdropKind> kind)
    {
        Attached.Add((window, kind));
        window.Closed += (_, _) => Attached.RemoveAll(a => a.Window == window || !a.Window.IsLoaded);
        window.SourceInitialized += (_, _) => Apply(window, kind(), DarkTheme);

        if (window.IsLoaded)
        {
            Apply(window, kind(), DarkTheme);
        }
    }

    /// <summary>Called by the ThemeManager after every palette swap.</summary>
    public static void SyncToTheme(bool dark)
    {
        DarkTheme = dark;

        for (var index = Attached.Count - 1; index >= 0; index--)
        {
            var (window, kind) = Attached[index];
            if (!window.IsLoaded)
            {
                Attached.RemoveAt(index);
                continue;
            }

            Apply(window, kind(), dark);
        }
    }

    private static void Apply(Window window, BackdropKind kind, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        TrySet(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
        TrySet(handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

        if (kind != BackdropKind.None)
        {
            TrySet(handle, DWMWA_SYSTEMBACKDROP_TYPE, kind == BackdropKind.Mica ? BACKDROP_MICA : BACKDROP_ACRYLIC);
        }
    }

    private static void TrySet(IntPtr handle, int attribute, int value)
    {
        DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
