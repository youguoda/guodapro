using System.Diagnostics;

namespace Shiyu.Windows;

/// <summary>
/// The foreground application's identity — the exclusion gate's input for
/// capture paths (O-17): before Shiyu simulates Ctrl+C in someone else's
/// window, it must know whose window that is. The name is the process name,
/// exactly what the clipboard monitor records and what SourceApp exclusion
/// rules are written against.
/// </summary>
public static class ForegroundApplication
{
    /// <summary>
    /// The foreground process name and executable path, or nulls when the
    /// foreground cannot be identified (no window, dying process, protected
    /// process). Callers treat "unknown" as "not excluded": a rule that
    /// cannot be evaluated excludes nothing.
    /// </summary>
    public static (string? Name, string? ExePath) Current()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return (null, null);
        }

        if (NativeMethods.GetWindowThreadProcessId(foreground, out var processId) == 0)
        {
            return (null, null);
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return (process.ProcessName, TryExecutablePath(process));
        }
        catch (ArgumentException)
        {
            // The process ended between reading its id and opening it.
            return (null, null);
        }
        catch (InvalidOperationException)
        {
            return (null, null);
        }
    }

    private static string? TryExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Elevated or protected processes keep their modules to
            // themselves; the name alone is still worth having.
            return null;
        }
    }
}
