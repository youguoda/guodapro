using Shiyu.Core;

namespace Shiyu.Core.Tests.Fakes;

/// <summary>
/// A clipboard and keyboard the test drives by hand, so the capture timing can
/// be examined without a real selection in a real application.
/// </summary>
public sealed class FakeCapturePlatform : ICapturePlatform
{
    private string? _clipboard;
    private uint _sequence;
    private int _waits;

    /// <summary>How many polls the target takes to answer. Null means never.</summary>
    public int? AnswersAfterPolls { get; set; }

    /// <summary>What the target puts on the clipboard when it answers.</summary>
    public string? Answer { get; set; } = "the selected text";

    public bool ReadFails { get; set; }
    public bool WriteFails { get; set; }
    public bool WriteThrows { get; set; }

    public List<string?> Writes { get; } = [];
    public int CopyKeystrokes { get; private set; }
    public int PasteKeystrokes { get; private set; }
    public int Polls { get; private set; }

    public void PutOnClipboard(string? text)
    {
        _clipboard = text;
        _sequence++;
    }

    public string? CurrentClipboard => _clipboard;

    public uint ClipboardSequenceNumber()
    {
        Polls++;
        return _sequence;
    }

    public string? ReadClipboardText()
        => ReadFails ? throw new ClipboardUnavailableException("read failed") : _clipboard;

    public bool WriteClipboardText(string? text)
    {
        if (WriteThrows)
        {
            throw new ClipboardUnavailableException("write failed");
        }

        Writes.Add(text);
        if (WriteFails)
        {
            return false;
        }

        _clipboard = text;
        _sequence++;
        return true;
    }

    public void SendCopyKeystroke() => CopyKeystrokes++;

    public void SendPasteKeystroke() => PasteKeystrokes++;

    public void Wait(TimeSpan duration)
    {
        _waits++;

        // The target application "responds" once the agreed number of polls has
        // gone by, which is how a slow application is simulated.
        if (AnswersAfterPolls is { } threshold && _waits == threshold)
        {
            _clipboard = Answer;
            _sequence++;
        }
    }
}
