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

    /// <summary>
    /// The registered formats an application uses to ask that its clipboard
    /// content be left out of clipboard history. Documented under "Cloud
    /// Clipboard and Clipboard History Formats"; honouring them is a
    /// convention rather than something Windows enforces, so it is Shiyu's
    /// own decision to respect them — and with an unencrypted history, not
    /// respecting them is not a defensible option.
    /// </summary>
    private static readonly uint ExcludeFromMonitorsFormat =
        NativeMethods.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");

    private static readonly uint CanIncludeInHistoryFormat =
        NativeMethods.RegisterClipboardFormatW("CanIncludeInClipboardHistory");

    // CanUploadToCloudClipboard is deliberately not consulted: it governs
    // synchronisation to the user's other devices, which Shiyu never does.
    // Treating it as a request for local secrecy would punch holes in the
    // history for content the user has every reason to expect to find there.

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

        // Self-suppression. Shiyu writes to the clipboard itself — re-copying
        // an entry from the library, and restoring what a capture borrowed —
        // and each of those writes comes back as a change notification. Owning
        // the clipboard is the exact test for "this one was mine", so Shiyu
        // never records its own hand.
        if (NativeMethods.GetClipboardOwner() == _window.Handle)
        {
            return;
        }

        // The foreground window is read first: opening the clipboard can take
        // several attempts, by which time focus may have moved on.
        var sourceApp = ForegroundProcessName();

        if (ReadClipboard() is { Text.Length: > 0 } reading)
        {
            Changed?.Invoke(new ClipboardSnapshot(reading.Text, sourceApp, reading.Excluded));
        }
    }

    private readonly record struct Reading(string Text, bool Excluded);

    /// <summary>
    /// Reads the text and the exclusion markers in a single open, retrying
    /// while another process holds the clipboard. Only one process may have it
    /// open at a time, so a failure here is ordinary contention, not an error.
    /// </summary>
    private Reading? ReadClipboard()
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
                var excluded = IsExcludedByMarker();

                // Read the markers even when there is no text: an application
                // that marked its content deserves the same answer either way.
                var text = ReadUnicodeText();
                return text is null ? null : new Reading(text, excluded);
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        return null;
    }

    /// <summary>Requires the clipboard to already be open.</summary>
    private static bool IsExcludedByMarker()
    {
        if (NativeMethods.IsClipboardFormatAvailable(ExcludeFromMonitorsFormat))
        {
            return true;
        }

        // Documented as a serialized DWORD: zero means keep it out of history,
        // one means the application explicitly wants it kept.
        return ReadDword(CanIncludeInHistoryFormat) == 0;
    }

    /// <summary>Requires the clipboard to already be open.</summary>
    private static uint? ReadDword(uint format)
    {
        if (!NativeMethods.IsClipboardFormatAvailable(format))
        {
            return null;
        }

        var handle = NativeMethods.GetClipboardData(format);
        if (handle == IntPtr.Zero || (ulong)NativeMethods.GlobalSize(handle) < sizeof(uint))
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
            return unchecked((uint)Marshal.ReadInt32(pointer));
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    /// <summary>Requires the clipboard to already be open.</summary>
    private static string? ReadUnicodeText()
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
