using System.Text.RegularExpressions;

namespace Shiyu.Core;

public enum ContentKind
{
    /// <summary>Prose in a language the user may want translated.</summary>
    NaturalLanguage,

    Code,
    Url,
    FilePath,

    /// <summary>Digits, punctuation, identifiers — nothing to translate.</summary>
    Mechanical,

    /// <summary>Prose already in the user's own language.</summary>
    NativeLanguage,
}

/// <summary>
/// Decides what a piece of copied text is, well enough to know whether offering
/// a translation would help or merely interrupt.
///
/// Statistics over characters, not a model: this runs on every single copy, and
/// a badge that costs hundreds of milliseconds to decide has already failed.
///
/// The rules err towards silence. A badge that fails to appear costs one press
/// of the escape hatch hotkey; a badge that appears over code, paths and
/// numbers all day makes the whole tool feel like something to switch off.
/// </summary>
public static partial class ContentClassifier
{
    /// <summary>
    /// Enough CJK and the text is the user's own language. The threshold is
    /// low on purpose: an English sentence with one Chinese word in it is
    /// still, for this user, something they can already read.
    /// </summary>
    private const double NativeScriptShare = 0.15;

    /// <summary>
    /// Punctuation this dense stops being prose. Ordinary English writing sits
    /// far below it even with quotes and parentheses.
    /// </summary>
    private const double CodePunctuationShare = 0.08;

    [GeneratedRegex(@"^\s*[a-z][a-z0-9+.\-]*://", RegexOptions.IgnoreCase)]
    private static partial Regex SchemeUrl { get; }

    [GeneratedRegex(@"^\s*www\.\S+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BareWww { get; }

    [GeneratedRegex(@"^\s*(?:[a-z]:[\\/]|\\\\[^\\]|\.{1,2}[\\/]|/(?:usr|etc|home|var|opt|tmp)/)",
        RegexOptions.IgnoreCase)]
    private static partial Regex PathLike { get; }

    /// <summary>Operators that essentially never occur in prose.</summary>
    [GeneratedRegex(@"=>|->|::|==|!=|&&|\|\||\+=|\breturn\b|\bfunction\b|\bconst\b|\bdef\b|\bclass\b|\bimport\b|\bpublic\b|\bvoid\b|</\w+>|\w+\(\)")]
    private static partial Regex CodeSignal { get; }

    public static ContentKind Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ContentKind.Mechanical;
        }

        var trimmed = text.Trim();

        if (SchemeUrl.IsMatch(trimmed) || BareWww.IsMatch(trimmed))
        {
            return ContentKind.Url;
        }

        if (PathLike.IsMatch(trimmed))
        {
            return ContentKind.FilePath;
        }

        // Unicode-aware on purpose: Chinese characters are letters. Testing for
        // A-Za-z here would classify every Chinese sentence as a bare number.
        if (!trimmed.Any(char.IsLetter))
        {
            return ContentKind.Mechanical;
        }

        if (CjkShare(trimmed) >= NativeScriptShare)
        {
            return ContentKind.NativeLanguage;
        }

        if (LooksLikeCode(trimmed))
        {
            return ContentKind.Code;
        }

        return ContentKind.NaturalLanguage;
    }

    private static bool LooksLikeCode(string text)
    {
        if (CodeSignal.IsMatch(text))
        {
            return true;
        }

        // A line ending in a brace or semicolon is a statement, not a sentence.
        foreach (var line in text.Split('\n'))
        {
            var end = line.TrimEnd();
            if (end.EndsWith(';') || end.EndsWith('{') || end.EndsWith('}'))
            {
                return true;
            }
        }

        var letters = 0;
        var punctuation = 0;
        foreach (var character in text)
        {
            if (char.IsLetter(character))
            {
                letters++;
            }
            else if (!char.IsWhiteSpace(character) && !IsProsePunctuation(character))
            {
                punctuation++;
            }
        }

        var total = letters + punctuation;
        return total > 0 && (double)punctuation / total >= CodePunctuationShare;
    }

    /// <summary>Punctuation that belongs in a sentence and says nothing about code.</summary>
    private static bool IsProsePunctuation(char character)
        => character is '.' or ',' or '!' or '?' or '\'' or '"' or ';' or ':' or '-' or '—' or '…';

    private static double CjkShare(string text)
    {
        var cjk = 0;
        var counted = 0;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || char.IsPunctuation(character))
            {
                continue;
            }

            counted++;
            if (IsCjk(character))
            {
                cjk++;
            }
        }

        return counted == 0 ? 0 : (double)cjk / counted;
    }

    private static bool IsCjk(char character) => character
        is >= '一' and <= '鿿'      // unified ideographs
        or >= '㐀' and <= '䶿'      // extension A
        or >= '぀' and <= 'ヿ'      // kana
        or >= '가' and <= '힯';     // hangul

    /// <summary>
    /// Whether a badge should be offered. Only prose the user likely cannot
    /// already read qualifies.
    ///
    /// This decides the badge and nothing else. Recording happens regardless:
    /// a history with holes in it, for reasons the user can neither see nor
    /// explain, is a worse failure than a badge that stayed quiet.
    /// </summary>
    public static bool DeservesBadge(string text)
        => Classify(text) == ContentKind.NaturalLanguage;

}
