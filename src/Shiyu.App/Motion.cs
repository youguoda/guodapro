using System.Windows.Media.Animation;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// The only place a WPF animation is configured. The two curves and two
/// duration tiers live in Core's DesignTokens; when Windows asks for reduced
/// motion the factories return instant transitions, so every state change
/// stays visible with no movement anywhere.
///
/// Appearing and leaving are deliberately asymmetric, and not by taste: a
/// layered window whose current surface is fully transparent gets no
/// composition ticks (ticket 04's badge finding), so a fade-in from opacity 0
/// freezes forever. Windows therefore show at full opacity; leaving gets the
/// fade, starting from a painted surface that composites fine.
/// </summary>
internal static class Motion
{
    private static readonly IEasingFunction Standard = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction Feature = new QuinticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>
    /// A fade to the given opacity. To-only, so an interrupted fade continues
    /// from wherever it currently is rather than snapping or stacking.
    /// </summary>
    public static DoubleAnimation Fade(double to, bool feature = false)
        => Double(to, feature);

    /// <summary>
    /// A size or offset change — the squeeze of a tray button, the slide of a
    /// handover. Same rules as <see cref="Fade"/>: one tier, one curve, zero
    /// when the system asks for stillness.
    /// </summary>
    public static DoubleAnimation Double(double to, bool feature = false)
    {
        var allowed = UiAnimation.Allowed();

        return new DoubleAnimation
        {
            To = to,
            Duration = MotionPlan.Duration(allowed, feature),
            EasingFunction = allowed ? (feature ? Feature : Standard) : null,
        };
    }
}
