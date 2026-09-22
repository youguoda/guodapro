namespace Shiyu.Core;

/// <summary>
/// The operating system operations capture needs. Behind a port because the
/// timing logic around them is the riskiest code in Shiyu and has to be
/// testable without a real keyboard.
/// </summary>
public interface ICapturePlatform
{
    /// <summary>
    /// Changes whenever anything writes to the clipboard. This is how capture
    /// learns the target application has answered: nothing tells us directly.
    /// </summary>
    uint ClipboardSequenceNumber();

    string? ReadClipboardText();

    /// <summary>Writes text back, or clears the clipboard when given null.</summary>
    bool WriteClipboardText(string? text);

    void SendCopyKeystroke();

    void SendPasteKeystroke();

    /// <summary>Explicit so tests can drive the polling loop deterministically.</summary>
    void Wait(TimeSpan duration);
}

public enum CaptureOutcome
{
    /// <summary>Text was captured.</summary>
    Captured,

    /// <summary>
    /// Nothing arrived before the deadline. An empty selection and an
    /// unresponsive application are indistinguishable from out here — in both
    /// cases the clipboard simply never changed — so they share one outcome
    /// rather than pretending to a certainty we do not have.
    /// </summary>
    NothingCaptured,

    /// <summary>The clipboard could not be read or written at all.</summary>
    ClipboardUnavailable,
}

/// <param name="ClipboardRestored">
/// Whether the user's own clipboard survived. False here is the one failure
/// the user would genuinely resent, so callers must surface it.
/// </param>
public sealed record CaptureResult(string? Text, CaptureOutcome Outcome, bool ClipboardRestored)
{
    public bool Succeeded => Outcome == CaptureOutcome.Captured;
}

public sealed record CaptureTiming(TimeSpan PollInterval, TimeSpan Timeout)
{
    /// <summary>
    /// Polling every 15 ms for up to 600 ms. Too short and slow applications —
    /// Electron, remote desktops, anything busy — never get their answer in;
    /// too long and a failed capture feels like the tool has hung. These are
    /// starting values, meant to be revisited against real applications.
    /// </summary>
    public static CaptureTiming Default { get; } =
        new(TimeSpan.FromMilliseconds(15), TimeSpan.FromMilliseconds(600));
}

/// <summary>
/// Borrows the clipboard to read the user's selection, then puts it back.
///
/// There is no notification when a target application finishes writing to the
/// clipboard, so this polls a sequence number until it moves or the deadline
/// passes. The one rule that outranks everything else: whatever happens, the
/// clipboard the user had is restored.
/// </summary>
public sealed class SelectionCapture(ICapturePlatform platform, CaptureTiming? timing = null)
{
    private readonly CaptureTiming _timing = timing ?? CaptureTiming.Default;

    public CaptureResult Capture()
    {
        string? borrowed;
        uint before;

        try
        {
            borrowed = platform.ReadClipboardText();
            before = platform.ClipboardSequenceNumber();
        }
        catch (ClipboardUnavailableException)
        {
            // Nothing was borrowed, so there is nothing to put back.
            return new CaptureResult(null, CaptureOutcome.ClipboardUnavailable, ClipboardRestored: true);
        }

        string? captured = null;
        var outcome = CaptureOutcome.NothingCaptured;

        try
        {
            platform.SendCopyKeystroke();

            for (var waited = TimeSpan.Zero; waited < _timing.Timeout; waited += _timing.PollInterval)
            {
                platform.Wait(_timing.PollInterval);

                if (platform.ClipboardSequenceNumber() == before)
                {
                    continue;
                }

                captured = platform.ReadClipboardText();

                // A changed clipboard holding nothing usable is still nothing
                // captured — never fall back to what was there before, which
                // would hand back stale content as if it were the selection.
                if (!string.IsNullOrEmpty(captured))
                {
                    outcome = CaptureOutcome.Captured;
                }

                break;
            }
        }
        catch (ClipboardUnavailableException)
        {
            outcome = CaptureOutcome.ClipboardUnavailable;
        }

        // Unconditional, and after every path above — each of them has already
        // taken the user's clipboard away, including the ones that threw.
        var restored = Restore(borrowed);

        return new CaptureResult(
            outcome == CaptureOutcome.Captured ? captured : null,
            outcome,
            restored);
    }

    /// <summary>
    /// Puts text into whatever window was in front, for the quick bar and the
    /// paste half of capture.
    /// </summary>
    public bool Paste(string text)
    {
        try
        {
            if (!platform.WriteClipboardText(text))
            {
                return false;
            }

            platform.SendPasteKeystroke();
            return true;
        }
        catch (ClipboardUnavailableException)
        {
            return false;
        }
    }

    private bool Restore(string? borrowed)
    {
        try
        {
            return platform.WriteClipboardText(borrowed);
        }
        catch (ClipboardUnavailableException)
        {
            return false;
        }
    }
}

/// <summary>Raised when the clipboard could not be opened at all.</summary>
public sealed class ClipboardUnavailableException(string message) : Exception(message);
