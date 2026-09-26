using System.Runtime.InteropServices;

namespace Shiyu.Windows;

/// <summary>
/// The Win11 panel look from the ticket 03 spike: DWM-rounded corners plus the
/// transient Acrylic backdrop, applied to a layered window — the one shape the
/// spike proved the material shows through.
///
/// Every call is best-effort: an older build that refuses an attribute is a
/// build that falls back to the panel's own translucent fill, not an error
/// worth showing. S_OK from the call is not enough (the spike's finding: a
/// stock window accepts the attribute and paints black), which is why callers
/// must keep AllowsTransparency on.
/// </summary>
public static class DwmEffects
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    private const int DwmwcpRound = 2;
    private const int DwmsbtTransientWindow = 3;

    public static void TryApplyPanel(IntPtr handle)
    {
        TrySet(handle, DwmwaWindowCornerPreference, DwmwcpRound);
        TrySet(handle, DwmwaSystemBackdropType, DwmsbtTransientWindow);
    }

    private static void TrySet(IntPtr handle, int attribute, int value)
    {
        try
        {
            _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (Exception)
        {
            // Dwmapi refusing an attribute on this build — the fallback look.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
