using System.Text.RegularExpressions;

namespace Shiyu.Core;

/// <summary>
/// Strips the packaging a general model wraps around a translation despite
/// being asked not to.
///
/// The prompt is the real defence; this is the belt to its braces. It removes
/// only unmistakable wrappers — a leading "Translation:" label, a trailing
/// "Note:" paragraph — and leaves anything ambiguous alone, because mangling a
/// correct translation is worse than passing a slightly chatty one through.
/// </summary>
public static partial class TranslationCleanup
{
    [GeneratedRegex(@"^\s*(?:translation|translated text|译文|翻译)\s*[:：]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingLabel { get; }

    [GeneratedRegex(@"\n\s*(?:note|notes|explanation|注|注释|说明)\s*[:：].*$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TrailingNote { get; }

    public static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = LeadingLabel.Replace(text.Trim(), string.Empty);
        cleaned = TrailingNote.Replace(cleaned, string.Empty);

        return Unquote(cleaned.Trim());
    }

    /// <summary>
    /// Removes quotes the model added around the whole result — but only when
    /// they wrap everything, so a translation that genuinely contains a quoted
    /// phrase survives intact.
    /// </summary>
    private static string Unquote(string text)
    {
        if (text.Length < 2)
        {
            return text;
        }

        var opens = text[0];
        var closes = text[^1];

        var wrapped =
            (opens == '"' && closes == '"')
            || (opens == '\'' && closes == '\'')
            || (opens == '“' && closes == '”')
            || (opens == '「' && closes == '」');

        if (!wrapped)
        {
            return text;
        }

        var inner = text[1..^1];
        return inner.Contains(opens) || inner.Contains(closes) ? text : inner;
    }
}
