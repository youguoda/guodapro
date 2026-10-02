namespace Shiyu.Core;

/// <summary>
/// Whether a <c>WM_SETTINGCHANGE</c> broadcast is worth re-reading the system
/// theme preference for (O-37).
///
/// Windows broadcasts the message for many unrelated setting groups and names
/// the group in <c>lParam</c>; the theme lives in "ImmersiveColorSet". Only
/// that group — and unlabelled broadcasts, which the older parts of the shell
/// still send for a theme flip — justify a registry read, and only while the
/// application is actually following the system. The decision is a pure
/// function of the mode and the area string, so it lives here, tested,
/// rather than in the window-procedure plumbing.
/// </summary>
public static class ThemeFollowPolicy
{
    /// <summary>The setting group Windows names when the colour mode changes.</summary>
    public const string ColourArea = "ImmersiveColorSet";

    /// <summary>
    /// True when the broadcast might mean the system theme moved: the mode is
    /// System, and the area is the colour group or missing entirely.
    /// </summary>
    public static bool ShouldRecheck(AppTheme mode, string? area)
        => mode == AppTheme.System && (area is null || area == ColourArea);
}
