namespace Shiyu.Windows;

/// <summary>
/// Remembers which window the user was in, so something that has to take focus
/// can give it back.
///
/// The quick bar's focus rule is the opposite of the badge's: it needs the
/// keyboard, so it must be activated — which means the window the user was
/// typing in has to be noted beforehand and restored before anything is
/// pasted, or the paste lands in the quick bar itself.
/// </summary>
public readonly record struct ForegroundWindow(IntPtr Handle)
{
    public static ForegroundWindow Current() => new(NativeMethods.GetForegroundWindow());

    public bool IsSomething => Handle != IntPtr.Zero;

    /// <summary>
    /// Brings the remembered window back to the front. Windows only allows
    /// this from a process that currently owns the foreground — which, having
    /// just shown the quick bar, Shiyu does.
    /// </summary>
    public bool Restore() => IsSomething && NativeMethods.SetForegroundWindow(Handle);
}
