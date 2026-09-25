using System.Text;

namespace Shiyu.Core;

/// <summary>
/// The "HTML Format" clipboard flavour and its header: a prologue of byte
/// offsets followed by UTF-8 markup. Reading takes the fragment those offsets
/// name; writing rebuilds the header around a fragment.
///
/// In Core as pure text work so the offsets — the part that silently corrupts
/// pastes when wrong — are pinned by tests rather than by hope.
/// </summary>
public static class ClipboardHtml
{
    private const string Prologue = """
        Version:0.9
        StartHTML:{0:0000000000}
        EndHTML:{1:0000000000}
        StartFragment:{2:0000000000}
        EndFragment:{3:0000000000}

        """;

    /// <summary>
    /// The fragment inside a CF_HTML payload, or the whole payload when no
    /// header names one. Offsets are byte offsets; the prologue is ASCII, so
    /// Latin-1 math on the decoded string matches the bytes exactly.
    /// </summary>
    public static string ExtractFragment(byte[] payload)
    {
        var latin = Encoding.Latin1.GetString(payload);

        var startFragment = Offset(latin, "StartFragment:");
        var endFragment = Offset(latin, "EndFragment:");

        if (startFragment is { } start && endFragment is { } end && start < end && end <= payload.Length)
        {
            return Encoding.UTF8.GetString(payload, start, end - start);
        }

        // No honest fragment to take: UTF-8 decode the lot and let the caller
        // treat it as the content.
        return Encoding.UTF8.GetString(payload).TrimEnd('\0');
    }

    /// <summary>Builds a CF_HTML payload around a fragment, offsets honest by construction.</summary>
    public static string WrapFragment(string fragment)
    {
        // The markers must be inside the fragment range so consumers that
        // honour them agree with consumers that honour the offsets.
        var body = $"<html><body>\r\n<!--StartFragment-->{fragment}<!--EndFragment-->\r\n</body></html>";

        var headerLength = Encoding.Latin1.GetByteCount(string.Format(
            Prologue, 0, 0, 0, 0));

        var startHtml = headerLength;
        var endHtml = startHtml + Encoding.Latin1.GetByteCount(body);

        // Fragment offsets land on byte positions within the UTF-8 encoding
        // of the body; Latin-1 lengths for the ASCII markers, UTF-8 for the
        // content between them.
        var before = headerLength
            + Encoding.UTF8.GetByteCount("<html><body>\r\n<!--StartFragment-->");
        var after = headerLength + Encoding.UTF8.GetByteCount(
            "<html><body>\r\n<!--StartFragment-->" + fragment);

        var header = string.Format(
            Prologue, startHtml, endHtml, before, after);

        return header + body;
    }

    private static int? Offset(string latin, string marker)
    {
        var at = latin.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var digits = new StringBuilder();
        for (var i = at + marker.Length; i < latin.Length && digits.Length < 10; i++)
        {
            if (!char.IsDigit(latin[i]))
            {
                break;
            }

            digits.Append(latin[i]);
        }

        return digits.Length > 0 && int.TryParse(digits.ToString(), out var value) ? value : null;
    }
}
