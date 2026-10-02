using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Shiyu.Windows;

/// <summary>
/// A hidden window that exists only to receive messages. The application needs
/// one anyway for clipboard notifications, so the tray icon and — later — the
/// global hotkeys ride on the same window rather than each conjuring its own.
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private readonly string _className = "ShiyuMessageWindow_" + Guid.NewGuid().ToString("N");
    private readonly WindowProcedure _procedure;
    private readonly IntPtr _instance;
    private bool _disposed;

    /// <summary>
    /// <c>WM_SETTINGCHANGE</c>: Windows broadcasts it to every top-level
    /// window when a system setting moves — the theme among them (O-37).
    /// Published here because the constants NativeMethods carries are this
    /// assembly's own business.
    /// </summary>
    public const uint SettingChangeMessage = NativeMethods.WmSettingChange;

    /// <summary>
    /// Raised for every message. A handler that sets <c>Handled</c> stops the
    /// message reaching the default window procedure.
    /// </summary>
    public event Action<WindowMessage>? MessageReceived;

    public MessageWindow()
    {
        _instance = NativeMethods.GetModuleHandleW(null);

        // Kept in a field so the garbage collector cannot reclaim the delegate
        // while Windows still holds a pointer to it.
        _procedure = WindowProcedure;

        var windowClass = new NativeMethods.WindowClass
        {
            lpfnWndProc = _procedure,
            hInstance = _instance,
            lpszClassName = _className,
        };

        if (NativeMethods.RegisterClassW(ref windowClass) == 0)
        {
            throw new InvalidOperationException(
                "Could not register the message window class.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        // A top-level window that is never made visible, rather than a
        // message-only (HWND_MESSAGE) one. Message-only windows are lighter but
        // are excluded from broadcasts, and the shell notifications this window
        // carries — clipboard changes, the tray icon's callbacks, and the
        // TaskbarCreated broadcast the tray must handle to survive an Explorer
        // restart — are exactly the traffic that exclusion touches. The hidden
        // top-level window is the conventional host for both jobs.
        // WS_EX_TOOLWINDOW keeps it out of the taskbar and Alt-Tab.
        Handle = NativeMethods.CreateWindowExW(
            NativeMethods.WsExToolWindow, _className, null, NativeMethods.WsOverlapped,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);

        if (Handle == IntPtr.Zero)
        {
            // Read the error before cleaning up: UnregisterClassW would
            // overwrite it, and the exception would then describe the wrong
            // failure entirely.
            var error = Marshal.GetLastWin32Error();
            NativeMethods.UnregisterClassW(_className, _instance);
            throw new InvalidOperationException(
                "Could not create the message window.",
                new Win32Exception(error));
        }
    }

    public IntPtr Handle { get; }

    private IntPtr WindowProcedure(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        var received = new WindowMessage(message, wParam, lParam);
        MessageReceived?.Invoke(received);

        return received.Handled
            ? received.Result
            : NativeMethods.DefWindowProcW(hWnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (Handle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(Handle);
        }

        NativeMethods.UnregisterClassW(_className, _instance);
    }
}

/// <summary>One window message, and whatever a handler decided about it.</summary>
public sealed class WindowMessage(uint id, IntPtr wParam, IntPtr lParam)
{
    public uint Id { get; } = id;
    public IntPtr WParam { get; } = wParam;
    public IntPtr LParam { get; } = lParam;

    public bool Handled { get; private set; }
    public IntPtr Result { get; private set; }

    public void Handle(IntPtr result = default)
    {
        Handled = true;
        Result = result;
    }
}

