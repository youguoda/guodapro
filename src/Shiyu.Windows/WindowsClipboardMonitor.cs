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
    private long? _lastImageFingerprint;

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
        var (sourceApp, sourceExe) = ForegroundProcess();

        var reading = ReadClipboard();

        if (reading is { Text.Length: > 0 })
        {
            // Text supersedes whatever image came before it, so the next image
            // is judged fresh rather than against something long gone.
            _lastImageFingerprint = null;
            Changed?.Invoke(new ClipboardSnapshot(reading.Value.Text, sourceApp, reading.Value.Excluded)
            {
                SourceExePath = sourceExe,
            });
            return;
        }

        // No usable text. An image is the other thing worth keeping, and is
        // grabbed now rather than later: the clipboard is about to change again
        // and there is no second chance at it.
        if (WindowsClipboardImage.FromClipboard() is not { } image)
        {
            return;
        }

        // The same copy arrives more than once: an application publishes its
        // bitmap in several clipboard formats in turn, and each publication
        // raises its own notification with its own sequence number. Collapsing
        // a repeat of the previous image mirrors what the text path already
        // does; without it every copied image is recorded twice and written to
        // disk twice.
        var fingerprint = image.ContentFingerprint;
        if (fingerprint == _lastImageFingerprint)
        {
            return;
        }

        _lastImageFingerprint = fingerprint;
        Changed?.Invoke(new ClipboardSnapshot(string.Empty, sourceApp, IsExcluded(reading))
        {
            Image = image,
            SourceExePath = sourceExe,
        });
    }

    private readonly record struct Reading(string Text, bool Excluded);

    /// <summary>An exclusion marker applies to the whole clipboard, images included.</summary>
    private static bool IsExcluded(Reading? reading) => reading?.Excluded ?? false;

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

                // Returned even when there is no text, so the marker survives
                // for an image-only clipboard. Dropping the reading here would
                // quietly reopen the hole exclusion exists to close: an image
                // copied from a password manager would be recorded.
                return new Reading(ReadUnicodeText() ?? string.Empty, excluded);
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

    /// <summary>
    /// The foreground application's process name and executable path. The path
    /// is taken now — while the process is alive — because that is the only
    /// moment its icon is guaranteed extractable, and an uninstalled
    /// application's history should still show the icon it had.
    /// </summary>
    private static (string? Name, string? ExePath) ForegroundProcess()
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
        catch (Win32Exception)
        {
            // Elevated or protected processes keep their modules to
            // themselves; the entry is still recorded, the icon is not.
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
