using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Shiyu.Windows;

/// <summary>
/// The tray icon and its menu, via <c>Shell_NotifyIcon</c> and a Win32 popup
/// menu. Deliberately not WinForms' NotifyIcon: loading the whole of Windows
/// Forms into the process for one tray icon works against the memory budget
/// this application exists to respect.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint QuitCommandId = 1;
    private const uint FirstEntryCommandId = 100;

    /// <summary>
    /// Explorer broadcasts this when it restarts, having forgotten every tray
    /// icon. Without re-adding ours here, the icon would vanish for good and
    /// the application would still be running with no way to reach it.
    /// </summary>
    private static readonly uint TaskbarCreated =
        NativeMethods.RegisterWindowMessageW("TaskbarCreated");

    private readonly MessageWindow _window;
    private NativeMethods.NotifyIconData _data;
    private bool _added;
    private bool _disposed;

    /// <summary>Supplies the labels shown above the separator, newest first.</summary>
    public Func<IReadOnlyList<string>>? RecentItems { get; set; }

    public event Action? QuitRequested;

    public TrayIcon(MessageWindow window, string tooltip)
    {
        _window = window;
        _window.MessageReceived += OnMessage;

        _data = new NativeMethods.NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            hWnd = window.Handle,
            uID = 1,
            uFlags = NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip,
            uCallbackMessage = NativeMethods.WmTrayIcon,
            hIcon = NativeMethods.LoadIconW(IntPtr.Zero, NativeMethods.IdiApplication),
            szTip = Truncate(tooltip, 127),
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        _added = NativeMethods.Shell_NotifyIconW(NativeMethods.NimAdd, ref _data);
        if (!_added)
        {
            throw new InvalidOperationException(
                "Could not add the tray icon.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private void OnMessage(WindowMessage message)
    {
        if (message.Id == TaskbarCreated && !_disposed)
        {
            _added = NativeMethods.Shell_NotifyIconW(NativeMethods.NimAdd, ref _data);
            return;
        }

        if (message.Id != NativeMethods.WmTrayIcon)
        {
            return;
        }

        var trigger = (uint)(message.LParam.ToInt64() & 0xFFFF);
        if (trigger is NativeMethods.WmRightButtonUp or NativeMethods.WmLeftButtonUp)
        {
            message.Handle();
            ShowMenu();
        }
    }

    private void ShowMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var items = RecentItems?.Invoke() ?? [];

            if (items.Count == 0)
            {
                NativeMethods.AppendMenuW(
                    menu, NativeMethods.MfString | NativeMethods.MfGrayed, UIntPtr.Zero, "(还没有记录)");
            }
            else
            {
                for (var index = 0; index < items.Count; index++)
                {
                    NativeMethods.AppendMenuW(
                        menu,
                        NativeMethods.MfString | NativeMethods.MfGrayed,
                        new UIntPtr(FirstEntryCommandId + (uint)index),
                        MenuLabel(items[index]));
                }
            }

            NativeMethods.AppendMenuW(menu, NativeMethods.MfSeparator, UIntPtr.Zero, null);
            NativeMethods.AppendMenuW(menu, NativeMethods.MfString, new UIntPtr(QuitCommandId), "退出拾语");

            if (!NativeMethods.GetCursorPos(out var cursor))
            {
                return;
            }

            // Windows will not dismiss a tray menu on click-away unless the
            // owning window was brought to the foreground first, and needs a
            // message afterwards to finish tearing the menu down.
            NativeMethods.SetForegroundWindow(_window.Handle);

            var command = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd,
                cursor.X, cursor.Y, _window.Handle, IntPtr.Zero);

            NativeMethods.PostMessageW(_window.Handle, NativeMethods.WmNull, IntPtr.Zero, IntPtr.Zero);

            if (command == QuitCommandId)
            {
                QuitRequested?.Invoke();
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    /// <summary>
    /// Shows a balloon from the tray icon. Shiyu has no window to put a message
    /// in, so this is the only way it can say anything to the user.
    /// </summary>
    public void ShowNotification(string title, string message)
    {
        if (!_added)
        {
            return;
        }

        // A copy, so NIF_INFO does not stay set on the stored data and make
        // every later update pop a balloon of its own.
        var notification = _data;
        notification.uFlags = NativeMethods.NifInfo;
        notification.szInfoTitle = Truncate(title, 63);
        notification.szInfo = Truncate(message, 255);

        NativeMethods.Shell_NotifyIconW(NativeMethods.NimModify, ref notification);
    }

    /// <summary>Collapses newlines and clips, so one entry stays one menu row.</summary>
    private static string MenuLabel(string text)
    {
        var collapsed = string.Join(' ', text.Split(
            ['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // Ampersands would otherwise be read as keyboard accelerators.
        return Truncate(collapsed, 60).Replace("&", "&&");
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessage;

        if (_added)
        {
            NativeMethods.Shell_NotifyIconW(NativeMethods.NimDelete, ref _data);
            _added = false;
        }
    }
}
