using System.Runtime.InteropServices;
using Shiyu.Core;

namespace Shiyu.Windows;

/// <summary>
/// Takes Win+V over from the system clipboard panel, through a low-level
/// keyboard hook driven by the tested <see cref="WinVFilter"/>.
///
/// Two properties the takeover owes the user, both structural here:
/// the hook lives inside this process, so a crash or a kill removes it and
/// Win+V falls back to Windows on its own — nothing to clean up, nothing to
/// forget; and Dispose unhooks on the spot, so switching the setting off
/// restores the system behaviour without a restart.
///
/// The hook must be installed from a thread that pumps messages (the WPF UI
/// thread does); the callback itself only feeds the filter and marshals the
/// trigger onward.
/// </summary>
public sealed class WinVHook : IDisposable
{
    private const int WhKeyboardLl = 13;

    private readonly WinVFilter _filter = new();
    private readonly HookProc _proc;
    private readonly IntPtr _hook;
    private readonly SynchronizationContext? _context;

    private bool _disposed;

    public WinVHook()
    {
        _proc = OnHook;
        _context = SynchronizationContext.Current;
        _filter.Triggered += () => _context?.Post(_ => Triggered?.Invoke(), null);

        _hook = NativeMethods.SetWindowsHookExW(
            WhKeyboardLl, _proc, NativeMethods.GetModuleHandleW(null), 0);
    }

    /// <summary>Raised on the installing thread's context when Win+V is taken.</summary>
    public event Action? Triggered;

    private IntPtr OnHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

        // Injected keystrokes are deliberately NOT filtered: a user's own
        // automation (AutoHotkey and friends) sends Win+V the same way, and
        // Shiyu itself never synthesises this combination, so there is no
        // loop to guard against — only users to serve.
        var direction = wParam.ToInt64() switch
        {
            0x0100 or 0x0104 => KeyDirection.Down, // WM_KEYDOWN / WM_SYSKEYDOWN
            0x0101 or 0x0105 => KeyDirection.Up,   // WM_KEYUP / WM_SYSKEYUP
            _ => KeyDirection.Up,
        };

        var key = info.VkCode switch
        {
            0x5B => SpecialKey.LeftWin,
            0x5C => SpecialKey.RightWin,
            0x56 => SpecialKey.V,
            _ => SpecialKey.Other,
        };

        if (key == SpecialKey.Other && direction == KeyDirection.Up)
        {
            // Nothing the filter decides on; skip the call for speed.
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        return _filter.Feed(key, direction) == KeyFlow.Swallow
            ? new IntPtr(1)
            : NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    private static partial class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookExW(
            int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(
            IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string? lpModuleName);
    }
}
