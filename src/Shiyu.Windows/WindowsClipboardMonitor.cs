using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Shiyu.Core;

namespace Shiyu.Windows;

/// <summary>
/// Turns Windows clipboard notifications into <see cref="ClipboardSnapshot"/>.
///
/// Uses <c>AddClipboardFormatListener</c>, the modern notification mechanism —
/// not the legacy <c>SetClipboardViewer</c> chain, where one badly behaved
/// application can break notifications for everyone downstream of it.
/// </summary>
public sealed class WindowsClipboardMonitor : IClipboardMonitor, IDisposable
{
    private const int OpenAttempts = 8;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(15);

    private readonly MessageWindow _window;
    private bool _listening;
    private bool _disposed;

    public event Action<ClipboardSnapshot>? Changed;

    public WindowsClipboardMonitor(MessageWindow window)
    {
        _window = window;
        _window.MessageReceived += OnMessage;
        _listening = NativeMethods.AddClipboardFormatListener(_window.Handle);

        if (!_listening)
        {
            throw new InvalidOperationException(
                "Could not subscribe to clipboard notifications.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private void OnMessage(WindowMessage message)
    {
        if (message.Id != NativeMethods.WmClipboardUpdate)
        {
            return;
        }

        message.Handle();

        // The foreground window is read first: opening the clipboard can take
        // several attempts, by which time focus may have moved on.
        var sourceApp = ForegroundProcessName();

        if (ReadClipboardText() is { Length: > 0 } text)
        {
            Changed?.Invoke(new ClipboardSnapshot(text, sourceApp));
        }
    }

    /// <summary>
    /// Reads the clipboard as text, retrying while another process holds it.
    /// Only one process may have the clipboard open at a time, so a failure
    /// here is ordinary contention rather than an error.
    /// </summary>
    private string? ReadClipboardText()
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (!NativeMethods.OpenClipboard(_window.Handle))
            {
                Thread.Sleep(RetryDelay);
                continue;
            }

            try
            {
                var handle = NativeMethods.GetClipboardData(NativeMethods.CfUnicodeText);
                if (handle == IntPtr.Zero)
                {
                    return null;
                }

                var pointer = NativeMethods.GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return Marshal.PtrToStringUni(pointer);
                }
                finally
                {
                    NativeMethods.GlobalUnlock(handle);
                }
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        return null;
    }

    private static string? ForegroundProcessName()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return null;
        }

        if (NativeMethods.GetWindowThreadProcessId(foreground, out var processId) == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            // The process ended between reading its id and opening it.
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessage;

        if (_listening)
        {
            NativeMethods.RemoveClipboardFormatListener(_window.Handle);
            _listening = false;
        }
    }
}
