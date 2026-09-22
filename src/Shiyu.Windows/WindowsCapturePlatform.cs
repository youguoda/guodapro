using System.Runtime.InteropServices;
using Shiyu.Core;

namespace Shiyu.Windows;

/// <summary>
/// The Windows side of capture: synthesised keystrokes and the clipboard reads
/// and writes around them. The timing that uses all this lives in Core.
/// </summary>
public sealed class WindowsCapturePlatform(MessageWindow window, WindowsClipboardWriter writer)
    : ICapturePlatform
{
    private const int OpenAttempts = 8;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(10);

    private static readonly ushort[] ModifiersThatMustNotLeak =
        [NativeMethods.VkShift, NativeMethods.VkMenu, NativeMethods.VkLWin, NativeMethods.VkRWin];

    public uint ClipboardSequenceNumber() => NativeMethods.GetClipboardSequenceNumber();

    public string? ReadClipboardText()
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

        throw new ClipboardUnavailableException("The clipboard stayed locked by another process.");
    }

    /// <summary>
    /// Null clears the clipboard rather than writing an empty string, so an
    /// empty clipboard is restored as empty.
    ///
    /// Known limitation: only text is preserved. If the user had an image on
    /// the clipboard, the target application's own copy has already replaced it
    /// by the time capture reads anything — the technique cannot save what it
    /// never saw. Worth revisiting when images arrive (issue 09).
    /// </summary>
    public bool WriteClipboardText(string? text)
        => text is null ? writer.Clear() : writer.SetText(text);

    public void SendCopyKeystroke() => SendWithControl(NativeMethods.VkC);

    public void SendPasteKeystroke() => SendWithControl(NativeMethods.VkV);

    public void Wait(TimeSpan duration) => Thread.Sleep(duration);

    /// <summary>
    /// Sends Ctrl plus one key, having first let go of any modifier the user is
    /// still holding.
    ///
    /// This matters more than it looks. Capture runs from a hotkey, and the
    /// user's fingers are still on that hotkey's modifiers when it fires —
    /// press Ctrl+Shift+Z and the synthesised Ctrl+C arrives as Ctrl+Shift+C,
    /// which is a different command entirely in most applications.
    /// </summary>
    private static void SendWithControl(ushort key)
    {
        var sequence = new List<NativeMethods.Input>();

        foreach (var modifier in ModifiersThatMustNotLeak)
        {
            if (IsDown(modifier))
            {
                sequence.Add(Key(modifier, up: true));
            }
        }

        sequence.Add(Key(NativeMethods.VkControl, up: false));
        sequence.Add(Key(key, up: false));
        sequence.Add(Key(key, up: true));
        sequence.Add(Key(NativeMethods.VkControl, up: true));

        var inputs = sequence.ToArray();
        NativeMethods.SendInput(
            (uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
    }

    private static bool IsDown(ushort key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

    private static NativeMethods.Input Key(ushort key, bool up) => new()
    {
        type = NativeMethods.InputKeyboard,
        u = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KeyboardInput
            {
                wVk = key,
                dwFlags = up ? NativeMethods.KeyEventKeyUp : 0,
            },
        },
    };
}
