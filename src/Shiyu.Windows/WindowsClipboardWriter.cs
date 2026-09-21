using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Shiyu.Windows;

/// <summary>
/// Puts text on the clipboard on Shiyu's behalf — re-copying an entry from the
/// library, and later restoring what a capture borrowed.
/// </summary>
public sealed class WindowsClipboardWriter(MessageWindow window)
{
    private const int OpenAttempts = 8;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(15);

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

    private static IntPtr AllocateUnicode(string text)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(text + '\0');

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
