namespace Shiyu.Windows;

/// <summary>
/// Whether the user wants interface animation at all — Windows' "animation
/// effects" accessibility setting.
///
/// Read live on every use: a setting that only applies after a restart is one
/// the user believes they changed while the application believes otherwise,
/// and the read is a single syscall.
/// </summary>
public static class UiAnimation
{
    public static bool Allowed()
        => NativeMethods.SystemParametersInfo(
               NativeMethods.SpiGetClientAreaAnimation, 0, out var allowed, 0)
           && allowed;
}
