using System.Runtime.InteropServices;

namespace Shiyu.Windows;

/// <summary>
/// Returns pages the process no longer needs to the operating system.
///
/// A resident tray tool is judged by what Task Manager shows, and working
/// set is what Task Manager shows — after a collect, trimming it is the
/// honest last step of "release what we are not using".
/// </summary>
public static class MemoryTrim
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    public static void WorkingSet()
    {
        // -1, -1 empties the working set; pages come back on demand, which is
        // exactly the deal — pay a fault when used, cost nothing when idle.
        SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
    }
}
