using System.Text.RegularExpressions;

namespace Shiyu.Core;

public enum EntrySubtype
{
    None,

    Link,

    Email,

    Color,

    LocalPath,

    UncPath,
}

/// <summary>
/// What a text entry <em>is</em>, so the list can show it more usefully — a
/// link looks clickable, a colour shows its swatch, a path is honest about
/// being a path.
///
/// Deliberately separate from <see cref="ContentClassifier"/>: that one asks
/// "should the translation badge interrupt the user?" and errs towards
/// silence; this one asks "what does this look like?" and errs towards
/// precision. Different questions, different thresholds — merged, a change to
/// either answer would quietly damage the other.
/// </summary>
public static partial class SubtypeClassifier
{
    [GeneratedRegex(@"^\s*https?://[^\s]+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Link { get; }

    // The local part is generous on purpose: dots, underscores, pluses,
    // minuses and percent-escapes are all legal, and the reference
    // implementation shipped a bug for exactly this reason.
    [GeneratedRegex(@"^\s*[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\s*$")]
    private static partial Regex Email { get; }

    [GeneratedRegex(@"^\s*#[0-9A-Fa-f]{3}\s*$")]
    private static partial Regex ShortHex { get; }

    [GeneratedRegex(@"^\s*#[0-9A-Fa-f]{6}\s*$")]
    private static partial Regex LongHex { get; }

    [GeneratedRegex(@"^\s*rgba?\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*(,\s*(?:[01](?:\.\d+)?|\.\d+)\s*)?\)\s*$")]
    private static partial Regex RgbFunction { get; }

    [GeneratedRegex(@"^\s*[A-Za-z]:[\\/][^<>:""|?*]*\s*$")]
    private static partial Regex DrivePath { get; }

    [GeneratedRegex(@"^\s*\\{2}[^\\/\s]+[\\/][^\s]*\s*$")]
    private static partial Regex UncPath { get; }

    [GeneratedRegex(@"^\s*/(?:usr|etc|home|var|opt|tmp)/\S*\s*$")]
    private static partial Regex UnixPath { get; }

    public static EntrySubtype Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return EntrySubtype.None;
        }

        var trimmed = text.Trim();

        if (Link.IsMatch(trimmed))
        {
            return EntrySubtype.Link;
        }

        if (Email.IsMatch(trimmed))
        {
            return EntrySubtype.Email;
        }

        if (ShortHex.IsMatch(trimmed) || LongHex.IsMatch(trimmed) || RgbFunction.IsMatch(trimmed))
        {
            return EntrySubtype.Color;
        }

        if (UncPath.IsMatch(trimmed))
        {
            return EntrySubtype.UncPath;
        }

        if (DrivePath.IsMatch(trimmed) || UnixPath.IsMatch(trimmed))
        {
            return EntrySubtype.LocalPath;
        }

        return EntrySubtype.None;
    }
}

/// <summary>A parsed colour from a colour-typed entry, in bytes the UI can use directly.</summary>
public readonly record struct SubtypeColor(byte R, byte G, byte B, byte A)
{
    /// <summary>
    /// Parses the notations <see cref="SubtypeClassifier"/> calls a colour.
    /// Returns false for anything else rather than guessing.
    /// </summary>
    public static bool TryParse(string text, out SubtypeColor colour)
    {
        colour = default;

        var trimmed = text.Trim();

        if (trimmed.StartsWith('#'))
        {
            var digits = trimmed[1..];

            if (digits.Length == 3 && TryHex(digits[..1], digits[1..2], digits[2..3], out var r, out var g, out var b))
            {
                // #RGB is shorthand for doubled digits.
                colour = new((byte)(r * 17), (byte)(g * 17), (byte)(b * 17), 255);
                return true;
            }

            if (digits.Length == 6 && TryHex(digits[..2], digits[2..4], digits[4..6], out var rr, out var gg, out var bb))
            {
                colour = new(rr, gg, bb, 255);
                return true;
            }

            return false;
        }

        if (trimmed.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)
            && trimmed.EndsWith(')')
            && trimmed.Contains(','))
        {
            var inner = trimmed[trimmed.IndexOf('(')..].Trim('(', ')');
            var parts = inner.Split(',');

            if (parts.Length is 3 or 4
                && byte.TryParse(parts[0], out var red)
                && byte.TryParse(parts[1], out var green)
                && byte.TryParse(parts[2], out var blue))
            {
                byte alpha = 255;

                if (parts.Length == 4
                    && double.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out var ratio))
                {
                    alpha = (byte)Math.Clamp(Math.Round(ratio * 255), 0, 255);
                }

                colour = new(red, green, blue, alpha);
                return true;
            }
        }

        return false;

        static bool TryHex(string rr, string gg, string bb, out byte r, out byte g, out byte b)
        {
            r = 0;
            g = 0;
            b = 0;

            return byte.TryParse(rr, System.Globalization.NumberStyles.HexNumber, null, out r)
                && byte.TryParse(gg, System.Globalization.NumberStyles.HexNumber, null, out g)
                && byte.TryParse(bb, System.Globalization.NumberStyles.HexNumber, null, out b);
        }
    }
}
