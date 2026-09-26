using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// The content-layer entrance for transient surfaces: the surface fades in
/// from 0 and settles upward 8 device-independent units — while the window's
/// own opacity stays at 1, because a layered window animating from transparent
/// never composites (ticket 04's badge finding). The animation lives on the
/// content precisely so the window can keep the opacity the motion rule needs.
/// </summary>
internal static class Entrance
{
    public static void Play(FrameworkElement surface)
    {
        var duration = MotionPlan.Duration(UiAnimation.Allowed());
        if (duration == TimeSpan.Zero)
        {
            return;
        }

        var fade = new DoubleAnimation(0, 1, duration);
        var rise = new DoubleAnimation(DesignTokens.EntranceShift, 0, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };

        var transform = new TranslateTransform(0, DesignTokens.EntranceShift);
        surface.RenderTransform = transform;

        surface.BeginAnimation(UIElement.OpacityProperty, fade);
        transform.BeginAnimation(TranslateTransform.YProperty, rise);
    }
}
