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

    /// <summary>
    /// Applies the Win11 panel look and answers whether the system took it:
    /// true means DWM is painting the contour and the material, false means
    /// the caller is on its own — the overlay shells' cue (ticket 20) to
    /// draw their fallback stroke and shadow.
    /// </summary>
    public static bool TryApplyPanel(IntPtr handle)
    {
        var rounded = TrySet(handle, DwmwaWindowCornerPreference, DwmwcpRound);
        var material = TrySet(handle, DwmwaSystemBackdropType, DwmsbtTransientWindow);
        return rounded && material;
    }

    private static bool TrySet(IntPtr handle, int attribute, int value)
    {
        try
        {
            return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;
        }
        catch (Exception)
        {
            // expected: 这台构建的 Dwmapi 拒了该属性——回退外观。
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
