using System.ComponentModel;
using System.Runtime.InteropServices;
using Shiyu.Core;

namespace Shiyu.Windows;

/// <summary>
/// Puts text on the clipboard on Shiyu's behalf — re-copying an entry from the
/// library, and later restoring what a capture borrowed.
/// </summary>
public sealed class WindowsClipboardWriter(MessageWindow window)
{
    private const int OpenAttempts = 8;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(15);

    private static readonly uint HtmlFormat =
        NativeMethods.RegisterClipboardFormatW("HTML Format");

    private static readonly uint RtfFormat =
        NativeMethods.RegisterClipboardFormatW("Rich Text Format");

    /// <summary>
    /// Returns whether the text made it onto the clipboard. Failure here is
    /// ordinary contention — another process holding the clipboard — and the
    /// caller is expected to tell the user rather than pretend it worked.
    /// </summary>
    public bool SetText(string text)
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            // Opening with our own window makes Shiyu the clipboard owner,
            // which is exactly how the monitor recognises this write as its
            // own and declines to record it.
            if (!NativeMethods.OpenClipboard(window.Handle))
            {
                Thread.Sleep(RetryDelay);
                continue;
            }

            try
            {
                if (!NativeMethods.EmptyClipboard())
                {
                    return false;
                }

                var handle = AllocateUnicode(text);
                if (handle == IntPtr.Zero)
                {
                    return false;
                }

                if (NativeMethods.SetClipboardData(NativeMethods.CfUnicodeText, handle) == IntPtr.Zero)
                {
                    // Ownership did not transfer, so the block is still ours to
                    // account for; there is nothing useful left to do with it.
                    throw new InvalidOperationException(
                        "Could not place text on the clipboard.",
                        new Win32Exception(Marshal.GetLastWin32Error()));
                }

                return true;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        return false;
    }

    /// <summary>
    /// Writes a rich entry back: the plain text always, the HTML and RTF forms
    /// when the entry carries them. The plain form is written even on a rich
    /// paste on purpose — pasting into a plain editor must produce text, not
    /// blank, and a plain editor reading only the HTML format would otherwise
    /// find nothing it understands.
    /// </summary>
    public bool SetRich(string text, string? html, string? rtf)
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (!NativeMethods.OpenClipboard(window.Handle))
            {
                Thread.Sleep(RetryDelay);
                continue;
            }

            try
            {
                if (!NativeMethods.EmptyClipboard())
                {
                    return false;
                }

                var placed = false;

                // All formats in one open: the clipboard raises a single
                // change notification, and the monitor's owner check plus the
                // text fingerprint together keep the write from being recorded.
                var textHandle = AllocateUnicode(text);
                if (textHandle != IntPtr.Zero
                    && NativeMethods.SetClipboardData(NativeMethods.CfUnicodeText, textHandle) != IntPtr.Zero)
                {
                    placed = true;
                }

                if (html is { Length: > 0 })
                {
                    Place(HtmlFormat, System.Text.Encoding.UTF8.GetBytes(
                        ClipboardHtml.WrapFragment(html) + '\0'));
                }

                if (rtf is { Length: > 0 })
                {
                    Place(RtfFormat, System.Text.Encoding.UTF8.GetBytes(rtf + '\0'));
                }

                return placed;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        return false;

        void Place(uint format, byte[] bytes)
        {
            var handle = AllocateBytes(bytes);
            if (handle != IntPtr.Zero)
            {
                // Ownership rules as for text: a failure to place is tolerated
                // rather than throwing, because the plain fallback carries the
                // paste on its own.
                NativeMethods.SetClipboardData(format, handle);
            }
        }
    }

    /// <summary>Leaves the clipboard empty, as opposed to holding an empty string.</summary>
    public bool Clear()
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (!NativeMethods.OpenClipboard(window.Handle))
            {
                Thread.Sleep(RetryDelay);
                continue;
            }

            try
            {
                return NativeMethods.EmptyClipboard();
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        return false;
    }

    private static IntPtr AllocateUnicode(string text)
        => AllocateBytes(System.Text.Encoding.Unicode.GetBytes(text + '\0'));

    private static IntPtr AllocateBytes(byte[] bytes)
    {
        // GMEM_MOVEABLE is required: the clipboard takes ownership of the block
        // and frees it itself.
        var handle = NativeMethods.GlobalAlloc(NativeMethods.GmemMoveable, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }

        return handle;
    }
}
